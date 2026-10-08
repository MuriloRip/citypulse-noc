using System.Net;
using CityPulse.Api.Data;
using CityPulse.Api.Models;
using CityPulse.Api.Security;
using CityPulse.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace CityPulse.Api.Endpoints;

public static class AssetEndpoints
{
    public static IEndpointRouteBuilder MapAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/assets", GetAssetsAsync);
        endpoints.MapGet("/api/assets/{id:guid}", GetAssetAsync);
        endpoints.MapPost("/api/assets", (AssetRequest input, CityPulseDbContext db) => SaveAssetAsync(input, null, db));
        endpoints.MapPost("/api/assets/discovered/bulk", AddDiscoveredAssetsAsync);
        endpoints.MapPut("/api/assets/{id:guid}", (Guid id, AssetRequest input, CityPulseDbContext db) => SaveAssetAsync(input, id, db));
        endpoints.MapDelete("/api/assets/{id:guid}", DeleteAssetAsync);
        endpoints.MapPost("/api/assets/{id:guid}/status", UpdateAssetStatusAsync);

        return endpoints;
    }

    private static async Task<List<Asset>> GetAssetsAsync(CityPulseDbContext db) =>
        await db.Assets.AsNoTracking().OrderBy(asset => asset.Name).ToListAsync();

    private static async Task<IResult> GetAssetAsync(Guid id, CityPulseDbContext db)
    {
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id);
        return asset is null ? Results.NotFound() : Results.Ok(asset);
    }

    private static async Task<IResult> AddDiscoveredAssetsAsync(
        BulkDiscoveryImportRequest request,
        CityPulseDbContext db,
        CancellationToken cancellationToken)
    {
        if (!request.Authorized)
        {
            return Results.BadRequest(new { error = "Confirme a autorização para adicionar os dispositivos encontrados nesta rede." });
        }

        if (request.Devices is not { Length: > 0 and <= 254 })
        {
            return Results.BadRequest(new { error = "Selecione de 1 a 254 dispositivos para adicionar." });
        }

        var categories = new HashSet<string>(StringComparer.Ordinal)
        {
            "Impressora provável",
            "Câmera ou mídia provável",
            "Computador ou servidor provável",
            "Servidor ou dispositivo de rede",
            "Dispositivo com serviço de rede",
            "Dispositivo não identificado"
        };
        var normalizedDevices = new List<DiscoveredAssetInput>(request.Devices.Length);
        var seenAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in request.Devices)
        {
            if (device is null
                || !IPAddress.TryParse(device.Address, out var address)
                || !TargetPolicy.IsPrivateIpv4(address)
                || !address.ToString().Equals(device.Address, StringComparison.Ordinal)
                || !categories.Contains(device.Category)
                || !seenAddresses.Add(device.Address))
            {
                return Results.BadRequest(new { error = "A lista contém endereços privados ou categorias inválidas, ou dispositivos repetidos." });
            }

            normalizedDevices.Add(device);
        }

        var addresses = normalizedDevices.Select(device => device.Address).ToArray();
        var existingAddresses = await db.Assets
            .AsNoTracking()
            .Where(asset => addresses.Contains(asset.Address))
            .Select(asset => asset.Address)
            .ToListAsync(cancellationToken);
        var existingAddressSet = existingAddresses.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newAssets = normalizedDevices
            .Where(device => !existingAddressSet.Contains(device.Address))
            .Select(device => new Asset
            {
                Name = device.Address,
                Address = device.Address,
                Category = device.Category,
                Tier = "medium",
                Location = "Não informado",
                Protocol = ProbeProtocol.Icmp
            })
            .ToArray();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Assets.AddRange(newAssets);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new
        {
            addedCount = newAssets.Length,
            alreadyPresentCount = request.Devices.Length - newAssets.Length,
            assets = newAssets
        });
    }

    private static async Task<IResult> DeleteAssetAsync(Guid id, CityPulseDbContext db)
    {
        var asset = await db.Assets.FindAsync(id);
        if (asset is null)
        {
            return Results.NotFound();
        }

        db.Assets.Remove(asset);
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    private static async Task<IResult> UpdateAssetStatusAsync(
        Guid id,
        TriageRequest input,
        MonitoringService monitoring)
    {
        if (string.IsNullOrWhiteSpace(input.Resolution))
        {
            var started = await monitoring.StartTriageAsync(id);
            return started
                ? Results.Ok(new { status = AssetStatus.PendingTriage })
                : Results.NotFound();
        }

        var result = await monitoring.ResolveTriageAsync(id, input.Resolution);
        if (!result.Found)
        {
            return Results.NotFound(new { error = result.Error });
        }

        if (result.Error.Length > 0)
        {
            return Results.BadRequest(new { error = result.Error });
        }

        return Results.Ok(new { status = result.Status });
    }

    private static async Task<IResult> SaveAssetAsync(AssetRequest input, Guid? id, CityPulseDbContext db)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120
            || string.IsNullOrWhiteSpace(input.Category) || input.Category.Length > 60
            || string.IsNullOrWhiteSpace(input.Tier) || input.Tier is not ("high" or "medium" or "low")
            || input.Location?.Length > 120)
        {
            return Results.BadRequest(new
            {
                error = "Revise os campos: nome (até 120), categoria (até 60), localização (até 120) e criticidade válida."
            });
        }

        if (!TargetPolicy.IsAllowed(input.Address))
        {
            return Results.BadRequest(new
            {
                error = "Alvo não permitido. Use um IPv4 privado ou localhost, diretamente ou em uma URL HTTP(S)."
            });
        }

        if (input.ParentId is { } parentId && !await ParentRelationshipIsValidAsync(parentId, id, db))
        {
            return Results.BadRequest(new { error = "Ativo pai inválido ou a relação criaria um ciclo." });
        }

        var asset = id is null ? new Asset() : await db.Assets.FindAsync(id);
        if (asset is null)
        {
            return Results.NotFound();
        }

        asset.Name = input.Name.Trim();
        asset.Address = input.Address.Trim();
        asset.Category = input.Category.Trim();
        asset.Tier = input.Tier;
        asset.Location = string.IsNullOrWhiteSpace(input.Location)
            ? "Não informado"
            : input.Location.Trim();
        asset.ParentId = input.ParentId;
        asset.Protocol = input.Address.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? ProbeProtocol.Http
            : ProbeProtocol.Icmp;

        if (id is null)
        {
            db.Assets.Add(asset);
        }

        await db.SaveChangesAsync();
        return Results.Ok(asset);
    }

    private static async Task<bool> ParentRelationshipIsValidAsync(Guid parentId, Guid? assetId, CityPulseDbContext db)
    {
        var parents = await db.Assets
            .AsNoTracking()
            .ToDictionaryAsync(asset => asset.Id, asset => asset.ParentId);
        var visited = new HashSet<Guid>();
        Guid? current = parentId;

        while (current is { } currentParentId)
        {
            if (currentParentId == assetId
                || !visited.Add(currentParentId)
                || !parents.TryGetValue(currentParentId, out current))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record BulkDiscoveryImportRequest(bool Authorized, DiscoveredAssetInput[]? Devices);

public sealed record DiscoveredAssetInput(string Address, string Category);
