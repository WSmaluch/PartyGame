using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Content;
using PartyGame.Domain.Rooms;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Persistence.Seed;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Infrastructure.Rooms;

public sealed class DrawingAnswerPlannerTests
{
    [Fact]
    public async Task OnlyDrawingAnswer_ProducesOnlyDrawingQuestions()
    {
        await using var db = CreateDb(); await Seed(db); db.ChangeTracker.Clear(); var room = Room(4, [QuestionType.DrawingAnswer]);
        Assert.True(await new GamePlanner(db, new DeterministicRandom()).TryCreatePlanAsync(room, DateTimeOffset.UtcNow, default));
        Assert.All(room.Session!.Rounds.SelectMany(r => r.Questions), q => Assert.Equal(QuestionType.DrawingAnswer, q.Question.Type));
    }

    [Theory]
    [InlineData(4, 1, 1)]
    [InlineData(5, 1, 2)]
    [InlineData(6, 1, 2)]
    public async Task FourTypes_AreBalancedForFourToSixQuestions(int questionCount, int minimum, int maximum)
    {
        await using var db = CreateDb(); await Seed(db); db.ChangeTracker.Clear(); var room = Room(questionCount, [QuestionType.PlayerSelection, QuestionType.TextAnswer, QuestionType.PhotoAnswer, QuestionType.DrawingAnswer]);
        Assert.True(await new GamePlanner(db, new DeterministicRandom()).TryCreatePlanAsync(room, DateTimeOffset.UtcNow, default));
        var counts = room.Session!.Rounds.Single().Questions.GroupBy(q => q.Question.Type).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(4, counts.Count); Assert.All(counts.Values, count => Assert.InRange(count, minimum, maximum)); Assert.True(counts.Values.Max() - counts.Values.Min() <= 1); Assert.Equal(questionCount, counts.Values.Sum());
        Assert.Equal(questionCount, room.Session.Rounds.Single().Questions.Select(q => q.QuestionId).Distinct().Count());
    }

    [Fact]
    public async Task InsufficientEligibleContent_ReturnsControlledFalse()
    {
        await using var db = CreateDb(); await Seed(db); db.ChangeTracker.Clear(); var room = Room(4, [QuestionType.DrawingAnswer]);
        room.Players.RemoveRange(0, room.Players.Count); // no question meets a realistic game start
        Assert.False(await new GamePlanner(db, new DeterministicRandom()).TryCreatePlanAsync(room, DateTimeOffset.UtcNow, default));
    }

    private static PartyGameDbContext CreateDb() { var options = new DbContextOptionsBuilder<PartyGameDbContext>().UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared").Options; var db = new PartyGameDbContext(options); db.Database.OpenConnection(); db.Database.Migrate(); return db; }
    private static async Task Seed(PartyGameDbContext db) => await ContentSeeder.SeedAsync(db, new FixedClock());
    private static GameRoom Room(int questions, List<QuestionType> types)
    {
        var room = new GameRoom { Id = Guid.NewGuid(), Code = "PLAN", SelectedPackageKeys = ["starter"], EnabledQuestionTypes = types, Settings = new RoomSettings { RoundCount = 1, QuestionsPerRound = questions } };
        room.Players = Enumerable.Range(1, 3).Select(i => new Player { Id = Guid.NewGuid(), RoomId = room.Id, Room = room, Nickname = $"P{i}", NormalizedNickname = $"P{i}", IsConnected = true }).ToList(); room.HostPlayerId = room.Players[0].Id; return room;
    }
    private sealed class DeterministicRandom : IRandomProvider { public int Next(int minValue, int maxValue) => minValue; public int Next(int maxValue) => 0; public void Shuffle<T>(IList<T> list) { } }
    private sealed class FixedClock : IGameClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-07-21T12:00:00Z"); }
}
