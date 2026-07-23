using Microsoft.EntityFrameworkCore;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Infrastructure.Rooms;

public sealed class DrawingAnswerScoreCalculatorTests
{
    [Fact]
    public async Task DistributionSixThreeOne_AwardsVotersAndNoAuthorBonus()
    {
        await using var db = CreateDb();
        var (session, question, players) = CreateGame(db, 13);
        var drawings = players.Take(3).Select(p => AddSubmission(db, question, p)).ToArray();
        var voters = players.Skip(3).ToArray();
        for (var index = 0; index < voters.Length; index++) AddVote(db, question, voters[index], index < 6 ? drawings[0] : index < 9 ? drawings[1] : drawings[2]);
        await db.SaveChangesAsync();

        await new ScoreCalculator(db).CalculateAndApplyDrawingAnswerScoresAsync(session, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();

        Assert.All(players.Take(3), author => Assert.Equal(0, author.Score));
        Assert.All(voters.Take(6), voter => Assert.Equal(600, voter.Score));
        Assert.All(voters.Skip(6).Take(3), voter => Assert.Equal(300, voter.Score));
        Assert.Equal(100, voters.Last().Score);
        Assert.All(await db.ScoreTransactions.ToListAsync(), transaction => Assert.Equal("DrawingAnswerConformity", transaction.Reason));
    }

    [Fact]
    public async Task SelfVoteAndTie_AreAllowed()
    {
        await using var db = CreateDb();
        var (session, question, players) = CreateGame(db, 3);
        var drawings = players.Select(p => AddSubmission(db, question, p)).ToArray();
        for (var index = 0; index < players.Count; index++) AddVote(db, question, players[index], drawings[index]);
        await db.SaveChangesAsync();
        await new ScoreCalculator(db).CalculateAndApplyDrawingAnswerScoresAsync(session, DateTimeOffset.UtcNow, default);
        await db.SaveChangesAsync();
        Assert.All(players, player => Assert.Equal(100, player.Score));
    }

    [Fact]
    public async Task Calculation_IsIdempotentAndPlayerScoresMatchLedger()
    {
        await using var db = CreateDb();
        var (session, question, players) = CreateGame(db, 3);
        var drawing = AddSubmission(db, question, players[0]);
        foreach (var player in players) AddVote(db, question, player, drawing);
        await db.SaveChangesAsync();
        var calculator = new ScoreCalculator(db);
        await calculator.CalculateAndApplyDrawingAnswerScoresAsync(session, DateTimeOffset.UtcNow, default); await db.SaveChangesAsync();
        await calculator.CalculateAndApplyDrawingAnswerScoresAsync(session, DateTimeOffset.UtcNow, default); await db.SaveChangesAsync();
        Assert.Equal(3, await db.ScoreTransactions.CountAsync());
        Assert.All(players, p => Assert.Equal(300, p.Score));
        Assert.Equal(players.Sum(p => p.Score), await db.ScoreTransactions.SumAsync(t => t.Points));
    }

    private static PartyGameDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PartyGameDbContext>().UseSqlite($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared").Options;
        var db = new PartyGameDbContext(options); db.Database.OpenConnection(); db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;"); db.Database.Migrate(); return db;
    }

    private static (GameSession, GameQuestionInstance, List<Player>) CreateGame(PartyGameDbContext db, int count)
    {
        var package = new GamePackage { Id = Guid.NewGuid(), Key = $"P{Guid.NewGuid():N}" }; var category = new GameCategory { Id = Guid.NewGuid(), PackageId = package.Id, Key = $"C{Guid.NewGuid():N}", Package = package }; var definition = new GameQuestion { Id = Guid.NewGuid(), CategoryId = category.Id, Key = $"Q{Guid.NewGuid():N}", Type = QuestionType.DrawingAnswer, Category = category };
        var room = new GameRoom { Id = Guid.NewGuid(), Code = "DRAW", CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow }; var players = Enumerable.Range(1, count).Select(i => new Player { Id = Guid.NewGuid(), RoomId = room.Id, Nickname = $"P{i}", NormalizedNickname = $"P{i}", Room = room }).ToList(); room.Players = players; room.HostPlayerId = players[0].Id;
        var session = new GameSession { Id = Guid.NewGuid(), RoomId = room.Id, Room = room, CurrentQuestionInstanceId = Guid.NewGuid(), Stage = GameStage.CollectingDrawingAnswerVotes }; room.Session = session; var round = new GameRound { Id = Guid.NewGuid(), GameSessionId = session.Id, CategoryId = category.Id, Category = category, Session = session, RoundNumber = 1 }; session.Rounds.Add(round); var question = new GameQuestionInstance { Id = session.CurrentQuestionInstanceId.Value, RoundId = round.Id, QuestionId = definition.Id, Round = round, Question = definition, Stage = GameStage.CollectingDrawingAnswerVotes }; round.Questions.Add(question); db.AddRange(package, category, definition, room, session, round, question); return (session, question, players);
    }

    private static DrawingAnswerSubmission AddSubmission(PartyGameDbContext db, GameQuestionInstance question, Player author)
    {
        var asset = new MediaAsset { Id = Guid.NewGuid(), MediaKind = MediaKind.DrawingAnswer, RoomId = question.Round.Session.RoomId, PlayerId = author.Id, QuestionInstanceId = question.Id, DisplayStorageKey = $"{Guid.NewGuid():N}/display.png", ThumbnailStorageKey = $"{Guid.NewGuid():N}/thumbnail.png", ContentType = "image/png", Sha256 = new string('0', 64) }; var submission = new DrawingAnswerSubmission { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, AuthorPlayerId = author.Id, MediaAssetId = asset.Id, MediaAsset = asset, ClientSubmissionId = Guid.NewGuid() }; db.AddRange(asset, submission); return submission;
    }

    private static void AddVote(PartyGameDbContext db, GameQuestionInstance question, Player voter, DrawingAnswerSubmission drawing) => db.DrawingAnswerVotes.Add(new DrawingAnswerVote { Id = Guid.NewGuid(), QuestionInstanceId = question.Id, VoterPlayerId = voter.Id, SelectedDrawingAnswerId = drawing.Id });
}
