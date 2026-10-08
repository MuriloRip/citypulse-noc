using Microsoft.EntityFrameworkCore;

namespace CityPulse.Api.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(CityPulseDbContext db)
    {
        await db.Database.EnsureCreatedAsync();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await db.Database.OpenConnectionAsync();
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "PRAGMA table_info('Assets')";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }

        var hasAvailabilityHistory = columns.Contains("AvailabilityAvailableSeconds")
            && columns.Contains("AvailabilityObservedSeconds");
        await EnsureAssetColumnAsync(db, columns, "TriageStartedAtUtc");
        await EnsureAssetColumnAsync(db, columns, "TotalChecks");
        await EnsureAssetColumnAsync(db, columns, "SuccessfulChecks");
        await EnsureAssetColumnAsync(db, columns, "AvailabilityAvailableSeconds");
        await EnsureAssetColumnAsync(db, columns, "AvailabilityObservedSeconds");
        await EnsureAssetColumnAsync(db, columns, "AvailabilityRecordedAtUtc");

        if (!await db.Assets.AnyAsync())
        {
            return;
        }

        if (!hasAvailabilityHistory)
        {
            await db.Assets.ExecuteUpdateAsync(update => update.SetProperty(asset => asset.UptimePercent, 100));
        }

        await db.Assets.ExecuteUpdateAsync(
            update => update.SetProperty(asset => asset.AvailabilityRecordedAtUtc, DateTime.UtcNow));
    }

    private static async Task EnsureAssetColumnAsync(
        CityPulseDbContext db,
        HashSet<string> columns,
        string name)
    {
        if (columns.Contains(name))
        {
            return;
        }

        var statement = name switch
        {
            "TriageStartedAtUtc" => "ALTER TABLE Assets ADD COLUMN TriageStartedAtUtc TEXT NULL",
            "TotalChecks" => "ALTER TABLE Assets ADD COLUMN TotalChecks INTEGER NOT NULL DEFAULT 0",
            "SuccessfulChecks" => "ALTER TABLE Assets ADD COLUMN SuccessfulChecks INTEGER NOT NULL DEFAULT 0",
            "AvailabilityAvailableSeconds" => "ALTER TABLE Assets ADD COLUMN AvailabilityAvailableSeconds REAL NOT NULL DEFAULT 0",
            "AvailabilityObservedSeconds" => "ALTER TABLE Assets ADD COLUMN AvailabilityObservedSeconds REAL NOT NULL DEFAULT 0",
            "AvailabilityRecordedAtUtc" => "ALTER TABLE Assets ADD COLUMN AvailabilityRecordedAtUtc TEXT NULL",
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };
        await db.Database.ExecuteSqlRawAsync(statement);
    }
}
