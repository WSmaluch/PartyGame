using System.Data;
using Microsoft.EntityFrameworkCore;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Api.Diagnostics;

public sealed record DatabaseSchemaStatus(
    string DatabaseSchemaVersion,
    string LatestSupportedSchemaVersion,
    string MinimumSupportedSchemaVersion,
    bool MigrationRequired,
    string DatabaseCompatibility);

public sealed class DatabaseSchemaService(PartyGameDbContext dbContext)
{
    public async Task<DatabaseSchemaStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var known = dbContext.Database.GetMigrations().Order(StringComparer.Ordinal).ToArray();
        if (known.Length == 0) throw new InvalidOperationException("No EF Core migrations are available.");
        var applied = await ReadAppliedMigrationsAsync(cancellationToken);
        var unknown = applied.Except(known, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var pending = known.Except(applied, StringComparer.Ordinal).Any();
        var compatibility = unknown.Length == 0 ? "compatible" : "newer-than-supported";
        return new DatabaseSchemaStatus(
            applied.Order(StringComparer.Ordinal).LastOrDefault() ?? "uninitialized",
            known[^1],
            known[0],
            pending,
            compatibility);
    }

    public async Task<DatabaseSchemaStatus> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var before = await GetStatusAsync(cancellationToken);
        if (before.DatabaseCompatibility != "compatible")
            throw new InvalidOperationException("Database schema is newer than this application supports.");
        await dbContext.Database.MigrateAsync(cancellationToken);
        return await GetStatusAsync(cancellationToken);
    }

    private async Task<string[]> ReadAppliedMigrationsAsync(CancellationToken cancellationToken)
    {
        var connection = dbContext.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened) await connection.OpenAsync(cancellationToken);
        try
        {
            await using var table = connection.CreateCommand();
            table.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory';";
            if (Convert.ToInt64(await table.ExecuteScalarAsync(cancellationToken)) == 0) return [];
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var applied = new List<string>();
            while (await reader.ReadAsync(cancellationToken)) applied.Add(reader.GetString(0));
            return applied.ToArray();
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
    }
}
