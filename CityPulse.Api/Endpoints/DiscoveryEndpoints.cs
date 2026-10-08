using CityPulse.Api.Services;

namespace CityPulse.Api.Endpoints;

public static class DiscoveryEndpoints
{
    public static IEndpointRouteBuilder MapDiscoveryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/discovery/scan", ScanNetworkAsync);
        return endpoints;
    }

    private static async Task<IResult> ScanNetworkAsync(
        DiscoveryScanRequest request,
        NetworkDiscoveryService discovery,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await discovery.ScanAsync(request.Cidr, request.Authorized, cancellationToken);
            return Results.Ok(result);
        }
        catch (DiscoveryValidationException exception)
        {
            return Results.BadRequest(new { error = exception.Message });
        }
        catch (DiscoveryBusyException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
    }
}

public sealed record DiscoveryScanRequest(string Cidr, bool Authorized);
