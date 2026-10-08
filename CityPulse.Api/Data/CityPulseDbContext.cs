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
