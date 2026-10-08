using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using CityPulse.Api.Data;
using CityPulse.Api.Models;
using CityPulse.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace CityPulse.Api.Services;

public sealed record ProbeResult(bool Success, AssetStatus Status, int? LatencyMs, string Type);

public sealed class MonitoringService(IServiceScopeFactory scopes, IHttpClientFactory clients, ILogger<MonitoringService> logger)
{
    private readonly SemaphoreSlim _cycleLock = new(1, 1);
    private const int HardStateFailures = 3;
    public DateTime LastPollAtUtc { get; private set; } = DateTime.UtcNow;
    public int Cycle { get; private set; }
    public TimeSpan TriageTimeout { get; } = TimeSpan.FromMinutes(5);

    public async Task RunPollCycleAsync(CancellationToken cancellationToken = default)
    {
        if (!await _cycleLock.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CityPulseDbContext>();
            var assets = await db.Assets.ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var asset in assets)
            {
                RecordAvailability(asset, now);
            }

            var expiredTriages = assets.Where(asset =>
                asset.Status == AssetStatus.PendingTriage
                && asset.TriageStartedAtUtc.HasValue
                && now - asset.TriageStartedAtUtc.Value >= TriageTimeout);
            foreach (var asset in expiredTriages)
            {
                asset.Status = AssetStatus.Down;
                asset.TriageStartedAtUtc = null;
                await OpenIncidentAsync(db, asset, "Triagem expirou após 5 minutos", "Falha técnica por ausência de resposta", cancellationToken);
            }

            var probeAssets = assets
                .Where(asset =>
                    asset.Status is not (AssetStatus.PendingTriage or AssetStatus.NoPower)
                    && !HasUnavailableAncestor(asset, assets))
                .ToList();
            var results = new ConcurrentDictionary<Guid, ProbeResult>();
            await Parallel.ForEachAsync(
                probeAssets,
                new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken },
                async (asset, ct) => results[asset.Id] = await ProbeAsync(asset, ct));

            foreach (var asset in probeAssets)
            {
                var result = results[asset.Id];
                asset.LastCheckedAtUtc = now;
                asset.TotalChecks++;

                if (result.Success)
                {
                    asset.SuccessfulChecks++;
                    asset.ConsecutiveFailures = 0;
                    asset.Status = result.Status;
                    asset.LatencyMs = result.LatencyMs;
                }
                else
                {
                    asset.ConsecutiveFailures++;
                    asset.LatencyMs = null;
                    if (result.Status == AssetStatus.Degraded || asset.ConsecutiveFailures >= HardStateFailures)
                    {
                        asset.Status = result.Status;
                    }
                }

                if (asset.Status == AssetStatus.Down)
                {
                    await OpenIncidentAsync(db, asset, asset.Name + " indisponível", result.Type, cancellationToken);
                }

                await ResolveIfOnlineAsync(db, asset, cancellationToken);
            }

            PropagateDependencyState(assets);

            await db.SaveChangesAsync(cancellationToken);
            LastPollAtUtc = now;
            Cycle++;
        }
        finally
        {
            _cycleLock.Release();
        }
    }

    public async Task<bool> StartTriageAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CityPulseDbContext>();
        var asset = await db.Assets.FindAsync([assetId], cancellationToken);
        if (asset is null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        RecordAvailability(asset, now);
        asset.Status = AssetStatus.PendingTriage;
        asset.TriageStartedAtUtc = now;
        asset.ConsecutiveFailures = 0;
        asset.LatencyMs = null;
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<(bool Found, string Error, AssetStatus? Status)> ResolveTriageAsync(Guid assetId, string? resolution, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CityPulseDbContext>();
        var asset = await db.Assets.FindAsync([assetId], cancellationToken);
        if (asset is null)
        {
            return (false, "Ativo não encontrado.", null);
        }

        var normalized = resolution?.Trim().ToUpperInvariant();
        var now = DateTime.UtcNow;
        RecordAvailability(asset, now);
        switch (normalized)
        {
            case "POWER_OUTAGE":
                asset.Status = AssetStatus.NoPower;
                asset.TriageStartedAtUtc = null;
                asset.ConsecutiveFailures = 0;
                await ResolveIfOnlineAsync(db, asset, cancellationToken, "Sem energia local confirmado");
                break;

            case "NETWORK_FAULT":
                asset.Status = AssetStatus.Down;
                asset.TriageStartedAtUtc = null;
                await OpenIncidentAsync(
                    db,
                    asset,
                    "Falha de equipamento confirmada",
                    "Falha de rede / SLA acionado",
                    cancellationToken);

                var children = await db.Assets
                    .Where(candidate => candidate.ParentId == asset.Id)
                    .ToListAsync(cancellationToken);
                foreach (var child in children)
                {
                    RecordAvailability(child, now);
                    child.Status = AssetStatus.Unreachable;
                }

                break;

            case "RESTORED":
                asset.Status = AssetStatus.Online;
                asset.TriageStartedAtUtc = null;
                asset.ConsecutiveFailures = 0;
                asset.LastCheckedAtUtc = now;
                asset.LatencyMs = null;
                await ResolveIfOnlineAsync(db, asset, cancellationToken, "Operação restaurada pelo operador");
                break;

            default:
                return (true, "Resolução inválida. Use POWER_OUTAGE, NETWORK_FAULT ou RESTORED.", null);
        }

        await db.SaveChangesAsync(cancellationToken);
        return (true, "", asset.Status);
    }

    private static void RecordAvailability(Asset asset, DateTime now)
    {
        if (asset.AvailabilityRecordedAtUtc is { } recordedAt)
        {
            var elapsedSeconds = Math.Max(0, (now - recordedAt).TotalSeconds);
            if (asset.Status != AssetStatus.PendingTriage)
            {
                asset.AvailabilityObservedSeconds += elapsedSeconds;
                if (asset.Status is AssetStatus.Online or AssetStatus.Degraded)
                    asset.AvailabilityAvailableSeconds += elapsedSeconds;
            }
        }

        asset.AvailabilityRecordedAtUtc = now;
        if (asset.AvailabilityObservedSeconds > 0)
        {
            asset.UptimePercent = asset.AvailabilityAvailableSeconds / asset.AvailabilityObservedSeconds * 100;
        }
    }

    private static bool HasUnavailableAncestor(Asset asset, IReadOnlyCollection<Asset> assets)
    {
        var byId = assets.ToDictionary(candidate => candidate.Id);
        var parentId = asset.ParentId;
        var visited = new HashSet<Guid>();
        while (parentId is not null
            && visited.Add(parentId.Value)
            && byId.TryGetValue(parentId.Value, out var parent))
        {
            if (parent.Status is AssetStatus.Down or AssetStatus.Unreachable)
                return true;
            parentId = parent.ParentId;
        }

        return false;
    }

    private static void PropagateDependencyState(IReadOnlyCollection<Asset> assets)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var asset in assets)
            {
                if (asset.Status != AssetStatus.Unreachable && HasUnavailableAncestor(asset, assets))
                {
                    asset.Status = AssetStatus.Unreachable;
                    changed = true;
                }
            }
        }
    }

    private static async Task OpenIncidentAsync(
        CityPulseDbContext db,
        Asset asset,
        string title,
        string type,
        CancellationToken cancellationToken)
    {
        var hasOpenIncident = await db.Incidents.AnyAsync(
            incident => incident.AssetId == asset.Id && incident.ResolvedAtUtc == null,
            cancellationToken);
        if (hasOpenIncident)
        {
            return;
        }

        db.Incidents.Add(new Incident
        {
            AssetId = asset.Id,
            Title = title,
            Type = type,
            Severity = asset.Tier switch
            {
                "high" => "high",
                "low" => "low",
                _ => "medium"
            }
        });
    }

    private static async Task ResolveIfOnlineAsync(
        CityPulseDbContext db,
        Asset asset,
        CancellationToken cancellationToken,
        string? note = null)
    {
        if (asset.Status is not (AssetStatus.Online or AssetStatus.NoPower))
        {
            return;
        }

        var openIncident = await db.Incidents.FirstOrDefaultAsync(
            incident => incident.AssetId == asset.Id && incident.ResolvedAtUtc == null,
            cancellationToken);
        if (openIncident is null)
        {
            return;
        }

        openIncident.ResolvedAtUtc = DateTime.UtcNow;
        openIncident.DurationMinutes = Math.Max(
            1,
            (int)(openIncident.ResolvedAtUtc.Value - openIncident.StartedAtUtc).TotalMinutes);
        if (note is not null)
        {
            openIncident.Type = note;
        }
    }

    private async Task<ProbeResult> ProbeAsync(Asset asset, CancellationToken ct)
    {
        if (!TargetPolicy.IsAllowed(asset.Address))
        {
            return new(false, AssetStatus.Down, null, "Alvo bloqueado pela política de destinos");
        }

        if (asset.Protocol == ProbeProtocol.Icmp)
        {
            using var ping = new Ping();
            try
            {
                var reply = await ping.SendPingAsync(asset.Address, 2500);
                return reply.Status == IPStatus.Success
                    ? new(true, AssetStatus.Online, (int)reply.RoundtripTime, "ICMP reply")
                    : new(false, AssetStatus.Down, null, $"ICMP {reply.Status}");
            }
            catch (Exception exception)
            {
                logger.LogDebug("ICMP probe failed with {FailureType}", exception.GetType().Name);
                return new(false, AssetStatus.Down, null, "Falha de rede");
            }
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.Address);
            using var response = await clients.CreateClient("probe")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var statusCode = (int)response.StatusCode;
            var status = statusCode >= 500
                ? AssetStatus.Down
                : statusCode >= 400
                    ? AssetStatus.Degraded
                    : AssetStatus.Online;

            return new(status != AssetStatus.Down, status, null, $"HTTP {statusCode}");
        }
        catch (Exception exception)
        {
            logger.LogDebug("HTTP probe failed with {FailureType}", exception.GetType().Name);
            return new(false, AssetStatus.Down, null, "Falha HTTP");
        }
    }
}

public sealed class PollingWorker(MonitoringService monitoring, ILogger<PollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await monitoring.RunPollCycleAsync(stoppingToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Polling cycle failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
