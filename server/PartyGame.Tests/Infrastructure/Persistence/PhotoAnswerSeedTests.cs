using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Content;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Persistence.Seed;

namespace PartyGame.Tests.Infrastructure.Persistence;

public sealed class PhotoAnswerSeedTests
{
    [Fact]
    public async Task Seed_IsIdempotent_AndCreatesFiftyQuestionsPerType()
    {
        var path = Path.Combine(Path.GetTempPath(), $"partygame-seed-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PartyGameDbContext>().UseSqlite($"Data Source={path}").Options;
            await using var db = new PartyGameDbContext(options);
            await db.Database.MigrateAsync();
            var clock = new FixedClock(DateTimeOffset.Parse("2026-07-21T12:00:00Z"));
            await ContentSeeder.SeedAsync(db, clock);
            await ContentSeeder.SeedAsync(db, clock);
            Assert.Equal(200, await db.GameQuestions.CountAsync());
            Assert.Equal(50, await db.GameQuestions.CountAsync(q => q.Type == QuestionType.PlayerSelection));
            Assert.Equal(50, await db.GameQuestions.CountAsync(q => q.Type == QuestionType.TextAnswer));
            Assert.Equal(50, await db.GameQuestions.CountAsync(q => q.Type == QuestionType.PhotoAnswer));
            Assert.Equal(50, await db.GameQuestions.CountAsync(q => q.Type == QuestionType.DrawingAnswer));
            Assert.Equal(10, await db.GameCategories.CountAsync(c => c.Questions.Count(q => q.Type == QuestionType.PhotoAnswer) == 5));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Seed_DoesNotOverwriteManuallyEditedQuestion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"partygame-seed-edit-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<PartyGameDbContext>().UseSqlite($"Data Source={path}").Options;
            await using var db = new PartyGameDbContext(options);
            await db.Database.MigrateAsync();
            var clock = new FixedClock(DateTimeOffset.Parse("2026-07-21T12:00:00Z"));
            await ContentSeeder.SeedAsync(db, clock);
            var question = await db.GameQuestions.OrderBy(candidate => candidate.Key).FirstAsync(candidate => candidate.Type == QuestionType.PhotoAnswer);
            question.TextPl = "Ręcznie zmieniona treść";
            await db.SaveChangesAsync();

            await ContentSeeder.SeedAsync(db, clock);

            Assert.Equal("Ręcznie zmieniona treść", await db.GameQuestions.Where(candidate => candidate.Id == question.Id).Select(candidate => candidate.TextPl).SingleAsync());
            Assert.Equal(200, await db.GameQuestions.CountAsync());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IGameClock { public DateTimeOffset UtcNow => now; }
}
