using System.Text.Json;
using System.Text.Json.Serialization;
using CityPulse.Api.Data;
using CityPulse.Api.Models;
using CityPulse.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<CityPulseDbContext>(o => o.UseSqlite("Data Source=citypulse.db"));
builder.Services.AddHttpClient("probe", c => { c.Timeout = TimeSpan.FromSeconds(5); c.DefaultRequestHeaders.UserAgent.ParseAdd("CityPulse/1.0"); });
builder.Services.AddSingleton<MonitoringService>();
builder.Services.AddHostedService<PollingWorker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
var app = builder.Build();
using (var scope = app.Services.CreateScope()) await SeedData.EnsureAsync(scope.ServiceProvider.GetRequiredService<CityPulseDbContext>());
app.UseDefaultFiles(); app.UseStaticFiles(); app.UseSwagger(); app.UseSwaggerUI();

app.MapGet("/api/assets", async (CityPulseDbContext db) => await db.Assets.AsNoTracking().OrderBy(x => x.Name).ToListAsync());
app.MapGet("/api/assets/{id:guid}", async (Guid id, CityPulseDbContext db) => await db.Assets.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id) is { } asset ? Results.Ok(asset) : Results.NotFound());
app.MapPost("/api/assets", async (AssetRequest input, CityPulseDbContext db) => await SaveAsset(input, null, db));
app.MapPut("/api/assets/{id:guid}", async (Guid id, AssetRequest input, CityPulseDbContext db) => await SaveAsset(input, id, db));
app.MapDelete("/api/assets/{id:guid}", async (Guid id, CityPulseDbContext db) => { var asset = await db.Assets.FindAsync(id); if (asset is null) return Results.NotFound(); db.Assets.Remove(asset); await db.SaveChangesAsync(); return Results.Ok(); });
app.MapPost("/api/poll", async (MonitoringService monitoring) => { await monitoring.RunPollCycleAsync(); return Results.Ok(); });
app.MapPost("/api/assets/{id:guid}/status", async (Guid id, TriageRequest input, MonitoringService monitoring) =>
{
    if (string.IsNullOrWhiteSpace(input.Resolution)) return await monitoring.StartTriageAsync(id) ? Results.Ok(new { status = AssetStatus.PendingTriage }) : Results.NotFound();
    var result = await monitoring.ResolveTriageAsync(id, input.Resolution);
    if (!result.Found) return Results.NotFound(new { error = result.Error });
    if (result.Error.Length > 0) return Results.BadRequest(new { error = result.Error });
    var resolvedStatus = input.Resolution.Equals("POWER_OUTAGE", StringComparison.OrdinalIgnoreCase) ? AssetStatus.NoPower : input.Resolution.Equals("RESTORE", StringComparison.OrdinalIgnoreCase) ? AssetStatus.Online : AssetStatus.Down;
    return Results.Ok(new { status = resolvedStatus });
});
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", runtime = ".NET 8", probe = "ICMP + HTTP real" }));
app.MapGet("/api/indicator", async (CityPulseDbContext db) =>
{
    var incidents = await db.Incidents.AsNoTracking().ToListAsync();
    var now = DateTime.UtcNow;
    var downtimeMinutes = incidents.Sum(x => (x.DurationMinutes ?? Math.Max(0, (int)(now - x.StartedAtUtc).TotalMinutes)));
    var measuredDurations = incidents.Select(x => x.DurationMinutes ?? Math.Max(0, (int)(now - x.StartedAtUtc).TotalMinutes)).ToList();
    var assets = await db.Assets.AsNoTracking().ToListAsync();
    return Results.Ok(new { indicator = "ISO 37120 10.04", totalChecks = assets.Sum(x => x.TotalChecks), downtimeMinutes, incidentCount = incidents.Count, activeIncidents = incidents.Count(x => x.ResolvedAtUtc is null), meanDowntimeMinutes = measuredDurations.Count == 0 ? 0 : Math.Round(measuredDurations.Average(), 1), availabilityPercent = assets.Count == 0 ? 100 : Math.Round(assets.Average(x => x.UptimePercent), 2), formula = "tempo total de indisponibilidade / número de incidentes" });
});
app.MapGet("/api/snapshot", async (CityPulseDbContext db, MonitoringService monitoring) =>
{
    var assets = await db.Assets.AsNoTracking().ToListAsync(); var incidents = await db.Incidents.AsNoTracking().OrderByDescending(x => x.StartedAtUtc).Take(20).ToListAsync(); var resolved = incidents.Where(x => x.DurationMinutes.HasValue).ToList();
    var availability = assets.Count == 0 ? 100 : assets.Average(x => x.UptimePercent); var mttr = resolved.Count == 0 ? 0 : (int)resolved.Average(x => x.DurationMinutes!.Value);
    return Results.Ok(new { availability, mttr, activeIncidents = incidents.Count(x => x.ResolvedAtUtc is null), totalAssets = assets.Count, online = assets.Count(x => x.Status == AssetStatus.Online), degraded = assets.Count(x => x.Status == AssetStatus.Degraded), pendingTriage = assets.Count(x => x.Status == AssetStatus.PendingTriage), noPower = assets.Count(x => x.Status == AssetStatus.NoPower), down = assets.Count(x => x.Status == AssetStatus.Down), unreachable = assets.Count(x => x.Status == AssetStatus.Unreachable), assets, incidents, lastPollAt = monitoring.LastPollAtUtc, cycle = monitoring.Cycle });
});
app.MapFallbackToFile("index.html");
app.Run();

static async Task<IResult> SaveAsset(AssetRequest input, Guid? id, CityPulseDbContext db)
{
    if (string.IsNullOrWhiteSpace(input.Name) || !TargetPolicy.IsAllowed(input.Address)) return Results.BadRequest(new { error = "Alvo inválido. Use IPv4 privado/municipal ou domínio gov.br." });
    if (input.ParentId == id || input.ParentId is not null && !await db.Assets.AnyAsync(x => x.Id == input.ParentId)) return Results.BadRequest(new { error = "Ativo pai inválido." });
    var asset = id is null ? new Asset() : await db.Assets.FindAsync(id);
    if (asset is null) return Results.NotFound();
    asset.Name = input.Name.Trim(); asset.Address = input.Address.Trim(); asset.Category = input.Category; asset.Tier = input.Tier; asset.Location = input.Location; asset.ParentId = input.ParentId; asset.Protocol = input.Address.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? ProbeProtocol.Http : ProbeProtocol.Icmp;
    if (id is null) db.Assets.Add(asset); await db.SaveChangesAsync(); return Results.Ok(asset);
}
