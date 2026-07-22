using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Infrastructure.Persistence;

public sealed class DrawingAnswerMigrationTests
{
    [Fact]
    public async Task EmptyDatabase_MigratesWithTablesIndexesAndForeignKeys()
    {
        await using var db = CreateDb(); await db.Database.MigrateAsync();
        var tables = await Names(db, "table"); var indexes = await Names(db, "index");
        Assert.Contains("DrawingAnswerEligiblePlayers", tables); Assert.Contains("DrawingAnswerSubmissions", tables); Assert.Contains("DrawingAnswerVoteEligiblePlayers", tables); Assert.Contains("DrawingAnswerVotes", tables);
        Assert.Contains("IX_DrawingAnswerSubmissions_QuestionInstanceId_ClientSubmissionId", indexes); Assert.Contains("IX_DrawingAnswerVotes_QuestionInstanceId_VoterPlayerId", indexes);
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA foreign_keys;"; Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task Phase4Database_UpgradesToStage5ACompletionMigration()
    {
        await using var db = CreateDb(); var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20260721155414_Phase4APhotoAnswers"); Assert.DoesNotContain("DrawingAnswerSubmissions", await Names(db, "table")); await migrator.MigrateAsync(); Assert.Contains("DrawingAnswerSubmissions", await Names(db, "table")); Assert.Contains("20260721191126_Stage5ACompletionFix", await db.Database.GetAppliedMigrationsAsync());
    }

    private static PartyGameDbContext CreateDb() { var options = new DbContextOptionsBuilder<PartyGameDbContext>().UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared").Options; var db = new PartyGameDbContext(options); db.Database.OpenConnection(); db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;"); return db; }
    private static Task<List<string>> Names(PartyGameDbContext db, string type) => type == "table"
        ? db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'").ToListAsync()
        : db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index'").ToListAsync();
}
