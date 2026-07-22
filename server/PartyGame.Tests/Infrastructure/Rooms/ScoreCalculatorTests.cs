using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Infrastructure.Rooms;

public class ScoreCalculatorTests
{
    private PartyGameDbContext GetDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<PartyGameDbContext>()
            .UseSqlite($"DataSource=file:{dbName}?mode=memory&cache=shared")
            .Options;

        var db = new PartyGameDbContext(options);
        db.Database.OpenConnection();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        db.Database.Migrate();
        return db;
    }

    private GameQuestionInstance SetupTestSession(PartyGameDbContext db, GameRoom room)
    {
        var package = new GamePackage { Id = Guid.NewGuid(), Key = "T", IsActive = true };
        db.GamePackages.Add(package);

        var category = new GameCategory { Id = Guid.NewGuid(), PackageId = package.Id, Key = "T", IsActive = true };
        db.GameCategories.Add(category);

        var session = new GameSession { Id = Guid.NewGuid(), RoomId = room.Id, CurrentQuestionInstanceId = Guid.NewGuid() };
        db.GameSessions.Add(session);
        room.Session = session;

        var round = new GameRound { Id = Guid.NewGuid(), GameSessionId = session.Id, CategoryId = category.Id, RoundNumber = 1 };
        db.GameRounds.Add(round);

        var questionDef = new GameQuestion { Id = Guid.NewGuid(), CategoryId = category.Id, Type = QuestionType.PlayerSelection, Key = "Q1", IsActive = true };
        db.GameQuestions.Add(questionDef);

        var question = new GameQuestionInstance { Id = session.CurrentQuestionInstanceId.Value, QuestionId = questionDef.Id, RoundId = round.Id };
        db.GameQuestionInstances.Add(question);

        foreach (var player in room.Players)
        {
            db.GameQuestionEligiblePlayers.Add(new GameQuestionEligiblePlayer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, PlayerId = player.Id });
        }

        return question;
    }

    [Fact]
    public async Task CalculateAndApplyScoresAsync_6_3_1_Distribution_AssignsCorrectScores()
    {
        var db = GetDbContext(Guid.NewGuid().ToString());
        var calc = new ScoreCalculator(db);

        var ania = Guid.NewGuid();
        var wojtek = Guid.NewGuid();
        var kasia = Guid.NewGuid();

        var votersForAnia = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToList();
        var votersForWojtek = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToList();
        var votersForKasia = Enumerable.Range(0, 1).Select(_ => Guid.NewGuid()).ToList();

        var room = new GameRoom { Id = Guid.NewGuid(), Code = "TEST" };
        db.GameRooms.Add(room);

        int i = 0;
        foreach (var p in new[] { ania, wojtek, kasia }.Concat(votersForAnia).Concat(votersForWojtek).Concat(votersForKasia))
        {
            room.Players.Add(new Player { Id = p, Nickname = $"P{++i}", NormalizedNickname = $"P{i}", IsHost = false, JoinedAtUtc = DateTimeOffset.UtcNow, Score = 0 });
        }

        var question = SetupTestSession(db, room);
        var session = room.Session;

        foreach (var voter in votersForAnia)
            question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = voter, SelectedPlayerId = ania });

        foreach (var voter in votersForWojtek)
            question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = voter, SelectedPlayerId = wojtek });

        foreach (var voter in votersForKasia)
            question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = voter, SelectedPlayerId = kasia });

        await db.SaveChangesAsync();

        // Act
        await calc.CalculateAndApplyScoresAsync(session!, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();

        // Assert
        var scores = await db.ScoreTransactions.ToListAsync();

        foreach (var voter in votersForAnia)
        {
            var p = room.Players.First(x => x.Id == voter);
            Assert.Equal(600, p.Score);
            Assert.Contains(scores, t => t.PlayerId == voter && t.Points == 600);
        }

        foreach (var voter in votersForWojtek)
        {
            var p = room.Players.First(x => x.Id == voter);
            Assert.Equal(300, p.Score);
            Assert.Contains(scores, t => t.PlayerId == voter && t.Points == 300);
        }

        foreach (var voter in votersForKasia)
        {
            var p = room.Players.First(x => x.Id == voter);
            Assert.Equal(100, p.Score);
            Assert.Contains(scores, t => t.PlayerId == voter && t.Points == 100);
        }
    }

    [Fact]
    public async Task CalculateAndApplyScoresAsync_MutualVoting_NoBonus()
    {
        var db = GetDbContext(Guid.NewGuid().ToString());
        var calc = new ScoreCalculator(db);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var room = new GameRoom { Id = Guid.NewGuid(), Code = "TEST" };
        db.GameRooms.Add(room);
        room.Players.Add(new Player { Id = a, Nickname = "A", NormalizedNickname = "A", Score = 0 });
        room.Players.Add(new Player { Id = b, Nickname = "B", NormalizedNickname = "B", Score = 0 });

        var question = SetupTestSession(db, room);
        var session = room.Session;

        question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = a, SelectedPlayerId = b });
        question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = b, SelectedPlayerId = a });

        await db.SaveChangesAsync();

        // Act
        await calc.CalculateAndApplyScoresAsync(session!, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();

        // Assert
        var scores = await db.ScoreTransactions.ToListAsync();
        Assert.Equal(2, scores.Count);

        Assert.Equal(100, room.Players.First(p => p.Id == a).Score); // A voted for B, B got 1 vote -> 100 points
        Assert.Equal(100, room.Players.First(p => p.Id == b).Score); // B voted for A, A got 1 vote -> 100 points
    }

    [Fact]
    public async Task CalculateAndApplyScoresAsync_SelfVote_NormalScore()
    {
        var db = GetDbContext(Guid.NewGuid().ToString());
        var calc = new ScoreCalculator(db);

        var a = Guid.NewGuid();

        var room = new GameRoom { Id = Guid.NewGuid(), Code = "TEST" };
        db.GameRooms.Add(room);
        room.Players.Add(new Player { Id = a, Nickname = "A", NormalizedNickname = "A", Score = 0 });

        var question = SetupTestSession(db, room);
        var session = room.Session;

        question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = a, SelectedPlayerId = a });

        await db.SaveChangesAsync();

        // Act
        await calc.CalculateAndApplyScoresAsync(session!, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();

        // Assert
        var scores = await db.ScoreTransactions.ToListAsync();
        Assert.Single(scores);
        Assert.Equal(100, room.Players.First(p => p.Id == a).Score);
    }

    [Fact]
    public async Task CalculateAndApplyScoresAsync_NoAnswer_ZeroPoints()
    {
        var db = GetDbContext(Guid.NewGuid().ToString());
        var calc = new ScoreCalculator(db);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var room = new GameRoom { Id = Guid.NewGuid(), Code = "TEST" };
        db.GameRooms.Add(room);
        room.Players.Add(new Player { Id = a, Nickname = "A", NormalizedNickname = "A", Score = 0 });
        room.Players.Add(new Player { Id = b, Nickname = "B", NormalizedNickname = "B", Score = 0 });

        var question = SetupTestSession(db, room);
        var session = room.Session;

        // B votes for A. A does not vote.
        question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = b, SelectedPlayerId = a });

        await db.SaveChangesAsync();

        // Act
        await calc.CalculateAndApplyScoresAsync(session!, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();

        // Assert
        var scores = await db.ScoreTransactions.ToListAsync();
        Assert.Single(scores); // Only B gets points
        Assert.Equal(0, room.Players.First(p => p.Id == a).Score);
        Assert.Equal(100, room.Players.First(p => p.Id == b).Score);
    }

    [Fact]
    public async Task CalculateAndApplyScoresAsync_Tie_NormalScore()
    {
        var db = GetDbContext(Guid.NewGuid().ToString());
        var calc = new ScoreCalculator(db);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        var v3 = Guid.NewGuid();
        var v4 = Guid.NewGuid();

        var room = new GameRoom { Id = Guid.NewGuid(), Code = "TEST" };
        db.GameRooms.Add(room);
        room.Players.Add(new Player { Id = a, Nickname = "A", NormalizedNickname = "A", Score = 0 });
        room.Players.Add(new Player { Id = b, Nickname = "B", NormalizedNickname = "B", Score = 0 });
        room.Players.Add(new Player { Id = v1, Nickname = "V1", NormalizedNickname = "V1", Score = 0 });
        room.Players.Add(new Player { Id = v2, Nickname = "V2", NormalizedNickname = "V2", Score = 0 });
        room.Players.Add(new Player { Id = v3, Nickname = "V3", NormalizedNickname = "V3", Score = 0 });
        room.Players.Add(new Player { Id = v4, Nickname = "V4", NormalizedNickname = "V4", Score = 0 });

        var question = SetupTestSession(db, room);
        var session = room.Session;

        // 2 votes for A, 2 votes for B
        question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = v1, SelectedPlayerId = a });
        question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = v2, SelectedPlayerId = a });
        question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = v3, SelectedPlayerId = b });
        question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = v4, SelectedPlayerId = b });

        await db.SaveChangesAsync();

        // Act
        await calc.CalculateAndApplyScoresAsync(session!, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();

        // Assert
        Assert.Equal(200, room.Players.First(p => p.Id == v1).Score);
        Assert.Equal(200, room.Players.First(p => p.Id == v2).Score);
        Assert.Equal(200, room.Players.First(p => p.Id == v3).Score);
        Assert.Equal(200, room.Players.First(p => p.Id == v4).Score);
    }

    [Fact]
    public async Task CalculateAndApplyScoresAsync_IsIdempotent()
    {
        var db = GetDbContext(Guid.NewGuid().ToString());
        var calc = new ScoreCalculator(db);

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var room = new GameRoom { Id = Guid.NewGuid(), Code = "TEST" };
        db.GameRooms.Add(room);
        room.Players.Add(new Player { Id = a, Nickname = "A", NormalizedNickname = "A", Score = 0 });
        room.Players.Add(new Player { Id = b, Nickname = "B", NormalizedNickname = "B", Score = 0 });

        var question = SetupTestSession(db, room);
        var session = room.Session;

        question.Answers.Add(new PlayerSelectionAnswer { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = a, SelectedPlayerId = b });

        await db.SaveChangesAsync();

        // Act
        await calc.CalculateAndApplyScoresAsync(session!, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();

        var scoreAfterFirst = room.Players.First(p => p.Id == a).Score;
        Assert.Equal(100, scoreAfterFirst);

        await calc.CalculateAndApplyScoresAsync(session!, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();

        var scoreAfterSecond = room.Players.First(p => p.Id == a).Score;
        Assert.Equal(100, scoreAfterSecond); // Does not increase

        Assert.Single(await db.ScoreTransactions.ToListAsync());
    }
}
