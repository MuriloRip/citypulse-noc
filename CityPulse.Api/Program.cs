using System.Text.Json;
using System.Text.Json.Serialization;
using CityPulse.Api.Endpoints;
using CityPulse.Api.Data;
using CityPulse.Api.Models;
using CityPulse.Api.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
var connectionString = builder.Configuration.GetConnectionString("CityPulse") ?? "Data Source=citypulse.db";
builder.Services.AddDbContext<CityPulseDbContext>(o => o.UseSqlite(connectionString));
builder.Services.AddHttpClient("probe", c => { c.Timeout = TimeSpan.FromSeconds(5); c.DefaultRequestHeaders.UserAgent.ParseAdd("CityPulse/1.0"); })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddSingleton<MonitoringService>();
builder.Services.AddSingleton<NetworkDiscoveryService>();
builder.Services.AddHostedService<PollingWorker>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
var app = builder.Build();
if (!app.Environment.IsDevelopment() && !builder.Configuration.GetValue<bool>("Security:TrustedAccessProxy"))
    throw new InvalidOperationException("Production requires a trusted access proxy with authentication and TLS. Set Security__TrustedAccessProxy=true only after configuring that proxy.");
using (var scope = app.Services.CreateScope())
{
    await DatabaseInitializer.InitializeAsync(
        scope.ServiceProvider.GetRequiredService<CityPulseDbContext>());
}
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; font-src 'self'; img-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapAssetEndpoints();
app.MapOperationalEndpoints();
app.MapDiscoveryEndpoints();
app.MapFallbackToFile("index.html");
app.Run();
