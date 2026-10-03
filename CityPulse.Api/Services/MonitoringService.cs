using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
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

    public async Task RunPollCycleAsync(CancellationToken cancellationToken = default)
    {
        if (!await _cycleLock.WaitAsync(0, cancellationToken)) return;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CityPulseDbContext>();
            var assets = await db.Assets.ToListAsync(cancellationToken);
            var results = new ConcurrentDictionary<Guid, ProbeResult>();
            await Parallel.ForEachAsync(assets, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = cancellationToken }, async (asset, ct) =>
            {
                results[asset.Id] = await ProbeAsync(asset, ct);
            });
            foreach (var asset in assets)
            {
                var result = results[asset.Id];
                asset.LastCheckedAtUtc = DateTime.UtcNow;
                if (result.Success) { asset.ConsecutiveFailures = 0; asset.Status = result.Status; asset.LatencyMs = result.LatencyMs; }
                else { asset.ConsecutiveFailures++; asset.LatencyMs = null; if (result.Status == AssetStatus.Degraded || asset.ConsecutiveFailures >= HardStateFailures) asset.Status = result.Status; }
                var parent = asset.ParentId is null ? null : assets.FirstOrDefault(x => x.Id == asset.ParentId);
                if (parent?.Status is AssetStatus.Down or AssetStatus.Unreachable) asset.Status = AssetStatus.Unreachable;
                var open = await db.Incidents.FirstOrDefaultAsync(x => x.AssetId == asset.Id && x.ResolvedAtUtc == null, cancellationToken);
                if (asset.Status is AssetStatus.Down && open is null) db.Incidents.Add(new Incident { AssetId = asset.Id, Title = $"{asset.Name} indisponível", Type = result.Type, Severity = asset.Tier == "high" ? "high" : "medium" });
                if (open is not null && asset.Status == AssetStatus.Online) { open.ResolvedAtUtc = DateTime.UtcNow; open.DurationMinutes = Math.Max(1, (int)(open.ResolvedAtUtc.Value - open.StartedAtUtc).TotalMinutes); }
            }
            await db.SaveChangesAsync(cancellationToken);
            LastPollAtUtc = DateTime.UtcNow; Cycle++;
        }
        finally { _cycleLock.Release(); }
    }

    private async Task<ProbeResult> ProbeAsync(Asset asset, CancellationToken ct)
    {
        if (!TargetPolicy.IsAllowed(asset.Address)) return new(false, AssetStatus.Down, null, "Alvo bloqueado pela whitelist");
        if (asset.Protocol == ProbeProtocol.Icmp)
        {
            using var ping = new Ping();
            try { var reply = await ping.SendPingAsync(asset.Address, 2500); return reply.Status == IPStatus.Success ? new(true, AssetStatus.Online, (int)reply.RoundtripTime, "ICMP reply") : new(false, AssetStatus.Down, null, $"ICMP {reply.Status}"); }
            catch (Exception ex) { logger.LogDebug(ex, "ICMP probe failed for {Address}", asset.Address); return new(false, AssetStatus.Down, null, "Falha de rede"); }
        }
        try { using var request = new HttpRequestMessage(HttpMethod.Get, asset.Address); using var response = await clients.CreateClient("probe").SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); var status = (int)response.StatusCode >= 500 ? AssetStatus.Down : (int)response.StatusCode >= 400 ? AssetStatus.Degraded : AssetStatus.Online; return new(status != AssetStatus.Down, status, null, $"HTTP {(int)response.StatusCode}"); }
        catch (Exception ex) { logger.LogDebug(ex, "HTTP probe failed for {Address}", asset.Address); return new(false, AssetStatus.Down, null, "Falha HTTP"); }
    }
}

public sealed class PollingWorker(MonitoringService monitoring, ILogger<PollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await monitoring.RunPollCycleAsync(stoppingToken); } catch (Exception ex) { logger.LogError(ex, "Polling cycle failed"); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
