using CityPulse.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CityPulse.Api.Data;

public sealed class CityPulseDbContext(DbContextOptions<CityPulseDbContext> options) : DbContext(options)
{
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<Incident> Incidents => Set<Incident>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Asset>().HasKey(x => x.Id);
        modelBuilder.Entity<Asset>().Property(x => x.Status).HasConversion<string>();
        modelBuilder.Entity<Asset>().Property(x => x.Protocol).HasConversion<string>();
        modelBuilder.Entity<Asset>().HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Incident>().HasOne(x => x.Asset).WithMany().HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

public static class SeedData
{
    public static async Task EnsureAsync(CityPulseDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        try { await db.Database.ExecuteSqlRawAsync("ALTER TABLE Assets ADD COLUMN TriageStartedAtUtc TEXT NULL"); } catch { /* coluna já existe */ }
        if (await db.Assets.AnyAsync()) return;
        var gateway = new Asset { Name = "Gateway local de teste", Address = "127.0.0.1", Category = "Rede", Tier = "high", Location = "Servidor CityPulse", Protocol = ProbeProtocol.Icmp };
        var portal = new Asset { Name = "Portal gov.br de teste", Address = "https://www.gov.br", Category = "Serviços", Tier = "high", Location = "Internet", Protocol = ProbeProtocol.Http, Parent = gateway };
        var loopbackHttp = new Asset { Name = "API local HTTP", Address = "http://localhost:4173", Category = "Serviços", Tier = "medium", Location = "Servidor CityPulse", Protocol = ProbeProtocol.Http, Parent = gateway };
        db.AddRange(gateway, portal, loopbackHttp);
        await db.SaveChangesAsync();
    }
}
