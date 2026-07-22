using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Content;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Persistence.Seed;

namespace PartyGame.Tests.Infrastructure.Persistence;

public sealed class DrawingAnswerSeedTests
{
    [Fact]
    public async Task Seed_CreatesFiveDrawingsPerCategory_IsIdempotent_AndPreservesManualText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"partygame-drawing-seed-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PartyGameDbContext>().UseSqlite($"Data Source={path}").Options;
            await using var db = new PartyGameDbContext(options);
            await db.Database.MigrateAsync();
            var clock = new FixedClock(DateTimeOffset.Parse("2026-07-21T12:00:00Z"));
            await ContentSeeder.SeedAsync(db, clock);
            var edited = await db.GameQuestions.FirstAsync(q => q.Type == QuestionType.DrawingAnswer);
            var keys = await db.GameQuestions.Where(q => q.Type == QuestionType.DrawingAnswer).Select(q => q.Key).ToListAsync();
            edited.TextPl = "Ręcznie zmienione";
            await db.SaveChangesAsync();
            await ContentSeeder.SeedAsync(db, clock);

            Assert.Single(await db.GamePackages.ToListAsync());
            Assert.Equal(10, await db.GameCategories.CountAsync());
            Assert.Equal(200, await db.GameQuestions.CountAsync());
            Assert.Equal(50, await db.GameQuestions.CountAsync(q => q.Type == QuestionType.DrawingAnswer));
            Assert.Equal(10, await db.GameCategories.CountAsync(c => c.Questions.Count(q => q.Type == QuestionType.DrawingAnswer) == 5));
            Assert.Equal(50, keys.Distinct().Count());
            Assert.Equal("Ręcznie zmienione", await db.GameQuestions.Where(q => q.Id == edited.Id).Select(q => q.TextPl).SingleAsync());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IGameClock { public DateTimeOffset UtcNow => now; }
}
