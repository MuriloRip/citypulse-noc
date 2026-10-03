using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using CityPulse.Api.Data;
using CityPulse.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CityPulse.Api.Services;

public sealed record ProbeResult(bool Success, AssetStatus Status, int? LatencyMs, string Type);

public static class TargetPolicy
{
    public static bool IsAllowed(string address)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 253) return false;
        if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return uri.Host.EndsWith(".gov.br", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var bytes = ip.GetAddressBytes();
        return bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31);
    }
}

public sealed class MonitoringService(IServiceScopeFactory scopes, IHttpClientFactory clients, ILogger<MonitoringService> logger)
{
    private readonly SemaphoreSlim _cycleLock = new(1, 1);
    private const int HardStateFailures = 3;
    public DateTime LastPollAtUtc { get; private set; } = DateTime.UtcNow;
    public int Cycle { get; private set; }
    public TimeSpan TriageTimeout { get; } = TimeSpan.FromMinutes(5);

    public async Task RunPollCycleAsync(CancellationToken cancellationToken = default)
    {
        if (!await _cycleLock.WaitAsync(0, cancellationToken)) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CityPulseDbContext>();
            var assets = await db.Assets.ToListAsync(cancellationToken);
            var now = DateTime.UtcNow;
            foreach (var asset in assets.Where(x => x.Status == AssetStatus.PendingTriage && x.TriageStartedAtUtc.HasValue && now - x.TriageStartedAtUtc.Value >= TriageTimeout))
            {
                asset.Status = AssetStatus.Down;
                asset.TriageStartedAtUtc = null;
                await OpenIncidentAsync(db, asset, "Triagem expirou após 5 minutos", "Falha técnica por ausência de resposta", cancellationToken);
            }
            var probeAssets = assets.Where(x => x.Status is not (AssetStatus.PendingTriage or AssetStatus.NoPower) && !HasUnavailableAncestor(x, assets)).ToList();
            var results = new ConcurrentDictionary<Guid, ProbeResult>();
            await Parallel.ForEachAsync(probeAssets, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (asset, ct) => results[asset.Id] = await ProbeAsync(asset, ct));
            foreach (var asset in probeAssets)
            {
                var result = results[asset.Id]; asset.LastCheckedAtUtc = now;
                asset.TotalChecks++;
                if (result.Success) { asset.SuccessfulChecks++; asset.ConsecutiveFailures = 0; asset.Status = result.Status; asset.LatencyMs = result.LatencyMs; }
                else { asset.ConsecutiveFailures++; asset.LatencyMs = null; if (result.Status == AssetStatus.Degraded) asset.Status = result.Status; else if (asset.ConsecutiveFailures >= HardStateFailures) { asset.Status = AssetStatus.PendingTriage; asset.TriageStartedAtUtc = now; } }
                asset.UptimePercent = asset.TotalChecks == 0 ? 100 : Math.Round(100d * asset.SuccessfulChecks / asset.TotalChecks, 2);
                if (asset.Status == AssetStatus.Down) await OpenIncidentAsync(db, asset, asset.Name + " indisponível", result.Type, cancellationToken);
                await ResolveIfOnlineAsync(db, asset, cancellationToken);
            }
            PropagateDependencyState(assets);
            await db.SaveChangesAsync(cancellationToken);
            LastPollAtUtc = now; Cycle++;
        }
        finally { _cycleLock.Release(); }
    }

    public async Task<bool> StartTriageAsync(Guid assetId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<CityPulseDbContext>(); var asset = await db.Assets.FindAsync([assetId], cancellationToken); if (asset is null) return false;
        asset.Status = AssetStatus.PendingTriage; asset.TriageStartedAtUtc = DateTime.UtcNow; asset.ConsecutiveFailures = 0; asset.LatencyMs = null; await db.SaveChangesAsync(cancellationToken); return true;
    }

    public async Task<(bool Found, string Error)> ResolveTriageAsync(Guid assetId, string? resolution, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<CityPulseDbContext>(); var asset = await db.Assets.FindAsync([assetId], cancellationToken); if (asset is null) return (false, "Ativo não encontrado.");
        var normalized = resolution?.Trim().ToUpperInvariant();
        if (normalized == "POWER_OUTAGE") { asset.Status = AssetStatus.NoPower; asset.TriageStartedAtUtc = null; asset.ConsecutiveFailures = 0; await ResolveIfOnlineAsync(db, asset, cancellationToken, "Sem energia local confirmado"); }
        else if (normalized == "NETWORK_FAULT") { asset.Status = AssetStatus.Down; asset.TriageStartedAtUtc = null; await OpenIncidentAsync(db, asset, "Falha de equipamento confirmada", "Falha de rede / SLA acionado", cancellationToken); var children = await db.Assets.Where(x => x.ParentId == asset.Id).ToListAsync(cancellationToken); foreach (var child in children) child.Status = AssetStatus.Unreachable; }
        else if (normalized == "RESTORE") { asset.Status = AssetStatus.Online; asset.TriageStartedAtUtc = null; asset.ConsecutiveFailures = 0; await ResolveIfOnlineAsync(db, asset, cancellationToken, "Operação restaurada pelo operador"); }
        else return (true, "Resolution must be POWER_OUTAGE, NETWORK_FAULT or RESTORE.");
        await db.SaveChangesAsync(cancellationToken); return (true, "");
    }

    private static async Task OpenIncidentAsync(CityPulseDbContext db, Asset asset, string title, string type, CancellationToken ct) { if (!await db.Incidents.AnyAsync(x => x.AssetId == asset.Id && x.ResolvedAtUtc == null, ct)) db.Incidents.Add(new Incident { AssetId = asset.Id, Title = title, Type = type, Severity = asset.Tier == "high" ? "high" : "medium" }); }
    private static async Task ResolveIfOnlineAsync(CityPulseDbContext db, Asset asset, CancellationToken ct, string? note = null) { if (asset.Status is not (AssetStatus.Online or AssetStatus.NoPower)) return; var open = await db.Incidents.FirstOrDefaultAsync(x => x.AssetId == asset.Id && x.ResolvedAtUtc == null, ct); if (open is not null) { open.ResolvedAtUtc = DateTime.UtcNow; open.DurationMinutes = Math.Max(1, (int)(open.ResolvedAtUtc.Value - open.StartedAtUtc).TotalMinutes); if (note is not null) open.Type = note; } }
    private static bool HasUnavailableAncestor(Asset asset, IReadOnlyCollection<Asset> assets)
    {
        var byId = assets.ToDictionary(x => x.Id); var parentId = asset.ParentId; var visited = new HashSet<Guid>();
        while (parentId is not null && visited.Add(parentId.Value) && byId.TryGetValue(parentId.Value, out var parent)) { if (parent.Status is AssetStatus.Down or AssetStatus.Unreachable) return true; parentId = parent.ParentId; }
        return false;
    }
    private static void PropagateDependencyState(IReadOnlyCollection<Asset> assets)
    {
        var changed = true; while (changed) { changed = false; foreach (var asset in assets) if (HasUnavailableAncestor(asset, assets) && asset.Status != AssetStatus.Unreachable) { asset.Status = AssetStatus.Unreachable; changed = true; } }
    }
    private async Task<ProbeResult> ProbeAsync(Asset asset, CancellationToken ct)
    {
        if (!TargetPolicy.IsAllowed(asset.Address)) return new(false, AssetStatus.Down, null, "Alvo bloqueado pela whitelist");
        if (asset.Protocol == ProbeProtocol.Icmp)
        {
            using var ping = new Ping(); try { var reply = await ping.SendPingAsync(asset.Address, 2500); return reply.Status == IPStatus.Success ? new(true, AssetStatus.Online, (int)reply.RoundtripTime, "ICMP reply") : new(false, AssetStatus.Down, null, $"ICMP {reply.Status}"); } catch (Exception ex) { logger.LogDebug(ex, "ICMP probe failed for {Address}", asset.Address); return new(false, AssetStatus.Down, null, "Falha de rede"); }
        }
        try { using var request = new HttpRequestMessage(HttpMethod.Get, asset.Address); using var response = await clients.CreateClient("probe").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); var status = (int)response.StatusCode >= 500 ? AssetStatus.Down : (int)response.StatusCode >= 400 ? AssetStatus.Degraded : AssetStatus.Online; return new(status != AssetStatus.Down, status, null, $"HTTP {(int)response.StatusCode}"); } catch (Exception ex) { logger.LogDebug(ex, "HTTP probe failed for {Address}", asset.Address); return new(false, AssetStatus.Down, null, "Falha HTTP"); }
    }
}

public sealed class PollingWorker(MonitoringService monitoring, ILogger<PollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) { while (!stoppingToken.IsCancellationRequested) { try { await monitoring.RunPollCycleAsync(stoppingToken); } catch (Exception ex) { logger.LogError(ex, "Polling cycle failed"); } await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } }
}
