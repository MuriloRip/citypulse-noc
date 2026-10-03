namespace CityPulse.Api.Models;

public enum AssetStatus { Online, Degraded, PendingTriage, NoPower, Down, Unreachable }
public enum ProbeProtocol { Icmp, Http }

public sealed class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Category { get; set; } = "Rede";
    public string Tier { get; set; } = "medium";
    public string Location { get; set; } = "Não informado";
    public Guid? ParentId { get; set; }
    public Asset? Parent { get; set; }
    public ProbeProtocol Protocol { get; set; }
    public AssetStatus Status { get; set; } = AssetStatus.Online;
    public int? LatencyMs { get; set; }
    public int ConsecutiveFailures { get; set; }
    public double UptimePercent { get; set; } = 100;
    public long TotalChecks { get; set; }
    public long SuccessfulChecks { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastCheckedAtUtc { get; set; }
    public DateTime? TriageStartedAtUtc { get; set; }
}

public sealed class Incident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public Asset Asset { get; set; } = null!;
    public string Title { get; set; } = "";
    public string Type { get; set; } = "";
    public string Severity { get; set; } = "medium";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }
    public int? DurationMinutes { get; set; }
}

public sealed record AssetRequest(string Name, string Address, string Category, string Tier, string Location, Guid? ParentId);
public sealed record TriageRequest(string? Resolution);
