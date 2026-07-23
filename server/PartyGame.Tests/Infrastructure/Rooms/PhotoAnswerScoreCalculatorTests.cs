using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Infrastructure.Rooms;

public sealed class PhotoAnswerScoreCalculatorTests
{
    [Fact]
    public async Task VotesAwardConformityToVoters_AllowSelfVote_AndAreIdempotent()
    {
        await using var db = CreateDb();
        var (session, question, players) = CreateGame(db);
        var first = AddSubmission(db, question, players[0]);
        AddSubmission(db, question, players[1]);
        foreach (var player in players)
            db.PhotoAnswerVotes.Add(new PhotoAnswerVote { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = player.Id, SelectedPhotoAnswerId = first.Id });
        await db.SaveChangesAsync();

        var calculator = new ScoreCalculator(db);
        await calculator.CalculateAndApplyPhotoAnswerScoresAsync(session, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();
        await calculator.CalculateAndApplyPhotoAnswerScoresAsync(session, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();

        Assert.All(players, player => Assert.Equal(300, player.Score));
        var ledger = await db.ScoreTransactions.ToListAsync();
        Assert.Equal(3, ledger.Count);
        Assert.All(ledger, entry =>
        {
            Assert.Equal(300, entry.Points);
            Assert.Equal("PhotoAnswerConformity", entry.Reason);
        });
    }

    private static PartyGameDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PartyGameDbContext>().UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared").Options;
        var db = new PartyGameDbContext(options);
        db.Database.OpenConnection();
        db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        db.Database.Migrate();
        return db;
    }

    private static (GameSession Session, GameQuestionInstance Question, List<Player> Players) CreateGame(PartyGameDbContext db)
    {
        var package = new GamePackage { Id = Guid.NewGuid(), Key = $"P{Guid.NewGuid():N}" };
        var category = new GameCategory { Id = Guid.NewGuid(), PackageId = package.Id, Key = $"C{Guid.NewGuid():N}", Package = package };
        var definition = new GameQuestion { Id = Guid.NewGuid(), CategoryId = category.Id, Key = $"Q{Guid.NewGuid():N}", Type = QuestionType.PhotoAnswer, Category = category };
        var room = new GameRoom { Id = Guid.NewGuid(), Code = "PHOT", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        var players = Enumerable.Range(1, 3).Select(i => new Player { Id = Guid.NewGuid(), RoomId = room.Id, Nickname = $"P{i}", NormalizedNickname = $"P{i}", Room = room }).ToList();
        room.Players = players;
        room.HostPlayerId = players[0].Id;
        var session = new GameSession { Id = Guid.NewGuid(), RoomId = room.Id, Room = room, CurrentQuestionInstanceId = Guid.NewGuid(), Stage = GameStage.CollectingPhotoAnswerVotes };
        room.Session = session;
        var round = new GameRound { Id = Guid.NewGuid(), GameSessionId = session.Id, CategoryId = category.Id, Category = category, Session = session, RoundNumber = 1 };
        session.Rounds.Add(round);
        var question = new GameQuestionInstance { Id = session.CurrentQuestionInstanceId.Value, RoundId = round.Id, QuestionId = definition.Id, Round = round, Question = definition, Stage = GameStage.CollectingPhotoAnswerVotes };
        round.Questions.Add(question);
        db.AddRange(package, category, definition, room, session, round, question);
        return (session, question, players);
    }

    private static PhotoAnswerSubmission AddSubmission(PartyGameDbContext db, GameQuestionInstance question, Player author)
    {
        var asset = new MediaAsset { Id = Guid.NewGuid(), MediaKind = MediaKind.PhotoAnswer, RoomId = question.Round.Session.RoomId, PlayerId = author.Id, QuestionInstanceId = question.Id, DisplayStorageKey = $"{Guid.NewGuid():N}/display.jpg", ThumbnailStorageKey = $"{Guid.NewGuid():N}/thumbnail.jpg", Sha256 = new string('0', 64) };
        var submission = new PhotoAnswerSubmission { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, AuthorPlayerId = author.Id, MediaAssetId = asset.Id, MediaAsset = asset, ClientSubmissionId = Guid.NewGuid() };
        db.AddRange(asset, submission);
        return submission;
    }
}
