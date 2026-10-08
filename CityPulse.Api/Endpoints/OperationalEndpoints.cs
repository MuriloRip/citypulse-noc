using CityPulse.Api.Data;
using CityPulse.Api.Models;
using CityPulse.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CityPulse.Api.Endpoints;

public static class OperationalEndpoints
{
    public static IEndpointRouteBuilder MapOperationalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/poll", RunPollCycleAsync);
        endpoints.MapGet("/api/health", GetHealth);
        endpoints.MapGet("/api/snapshot", GetSnapshotAsync);
        endpoints.MapGet("/api/indicator", GetIndicatorAsync);

        return endpoints;
    }

    private static async Task<IResult> RunPollCycleAsync(MonitoringService monitoring)
    {
        await monitoring.RunPollCycleAsync();
        return Results.Ok();
    }

    private static IResult GetHealth() => Results.Ok(new { status = "ok" });

    private static async Task<IResult> GetIndicatorAsync(
        CityPulseDbContext db,
        CancellationToken cancellationToken)
    {
        var incidents = await db.Incidents.AsNoTracking().ToListAsync(cancellationToken);
        var assets = await db.Assets.AsNoTracking().ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var durations = incidents
            .Select(incident => incident.DurationMinutes
                ?? Math.Max(0, (int)(now - incident.StartedAtUtc).TotalMinutes))
            .ToList();
        var observedSeconds = assets.Sum(asset => asset.AvailabilityObservedSeconds);
        var availableSeconds = assets.Sum(asset => asset.AvailabilityAvailableSeconds);

        return Results.Ok(new
        {
            indicator = "ISO 37120 10.04",
            totalChecks = assets.Sum(asset => asset.TotalChecks),
            downtimeMinutes = durations.Sum(),
            incidentCount = incidents.Count,
            activeIncidents = incidents.Count(incident => incident.ResolvedAtUtc is null),
            meanDowntimeMinutes = durations.Count == 0 ? 0 : Math.Round(durations.Average(), 1),
            availabilityPercent = observedSeconds == 0
                ? (double?)null
                : Math.Round(availableSeconds / observedSeconds * 100, 2),
            formula = "tempo total de indisponibilidade / número de incidentes"
        });
    }

    private static async Task<IResult> GetSnapshotAsync(
        CityPulseDbContext db,
        MonitoringService monitoring,
        CancellationToken cancellationToken)
    {
        var assets = await db.Assets.AsNoTracking().ToListAsync(cancellationToken);
        var incidents = await db.Incidents
            .AsNoTracking()
            .OrderByDescending(incident => incident.StartedAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken);
        var activeIncidents = await db.Incidents
            .CountAsync(incident => incident.ResolvedAtUtc == null, cancellationToken);
        var mttrAverage = await db.Incidents
            .Where(incident => incident.DurationMinutes.HasValue)
            .AverageAsync(incident => (double?)incident.DurationMinutes, cancellationToken);
        var mttr = mttrAverage is null ? null : (int?)mttrAverage.Value;
        var observedSeconds = assets.Sum(asset => asset.AvailabilityObservedSeconds);
        var availableSeconds = assets.Sum(asset => asset.AvailabilityAvailableSeconds);
        double? availability = observedSeconds == 0 ? null : availableSeconds / observedSeconds * 100;

        return Results.Ok(new
        {
            availability,
            mttr,
            activeIncidents,
            totalAssets = assets.Count,
            online = assets.Count(asset => asset.Status == AssetStatus.Online),
            degraded = assets.Count(asset => asset.Status == AssetStatus.Degraded),
            pendingTriage = assets.Count(asset => asset.Status == AssetStatus.PendingTriage),
            noPower = assets.Count(asset => asset.Status == AssetStatus.NoPower),
            down = assets.Count(asset => asset.Status == AssetStatus.Down),
            unreachable = assets.Count(asset => asset.Status == AssetStatus.Unreachable),
            assets,
            incidents,
            lastPollAt = monitoring.LastPollAtUtc,
            cycle = monitoring.Cycle
        });
    }
}
