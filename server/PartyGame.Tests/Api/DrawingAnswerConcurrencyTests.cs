using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class DrawingAnswerConcurrencyTests
{
    [Fact]
    public async Task SameClientSubmissionId_InParallelCreatesOneSubmissionAndAsset()
    {
        await using var harness = new PhotoAnswerTestHarness(); var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer); var id = Guid.NewGuid(); var png = await PhotoAnswerTestHarness.DrawingAsync();
        var responses = await Task.WhenAll(harness.UploadDrawingAsync(room, room.Players[0], png, clientSubmissionId: id), harness.UploadDrawingAsync(room, room.Players[0], png, clientSubmissionId: id));
        Assert.All(responses, response => Assert.True(response.IsSuccessStatusCode));
        var counts = await harness.DrawingCountsAsync(room.RoomCode); Assert.Equal((1, 1, 2, 11L), (counts.Submissions, counts.Assets, harness.FinalPngCount(), counts.Version));
    }

    [Fact]
    public async Task DifferentClientSubmissionIds_InParallelOneWinsWithoutOrphans()
    {
        await using var harness = new PhotoAnswerTestHarness(); var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer); var png = await PhotoAnswerTestHarness.DrawingAsync();
        var responses = await Task.WhenAll(harness.UploadDrawingAsync(room, room.Players[0], png, clientSubmissionId: Guid.NewGuid()), harness.UploadDrawingAsync(room, room.Players[0], png, clientSubmissionId: Guid.NewGuid()));
        Assert.Equal(1, responses.Count(response => response.IsSuccessStatusCode)); Assert.Equal(1, responses.Count(response => response.StatusCode == System.Net.HttpStatusCode.Conflict));
        var counts = await harness.DrawingCountsAsync(room.RoomCode); Assert.Equal((1, 1, 2), (counts.Submissions, counts.Assets, harness.FinalPngCount()));
    }

    [Fact]
    public async Task TwoVotesByOnePlayer_InParallelCreateOneVote()
    {
        await using var harness = new PhotoAnswerTestHarness(); var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer); var png = await PhotoAnswerTestHarness.DrawingAsync(); foreach (var player in room.Players) Assert.True((await harness.UploadDrawingAsync(room, player, png)).IsSuccessStatusCode); await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(10));
        Guid drawingId; await using (var scope = harness.Factory.Services.CreateAsyncScope()) drawingId = await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().DrawingAnswerSubmissions.Select(s => s.Id).FirstAsync();
        async Task<bool> Vote()
        {
            try { await using var scope = harness.Factory.Services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<IRoomService>().SubmitDrawingAnswerVoteAsync(room.RoomCode, room.Players[0].PlayerId, room.Players[0].Token, room.QuestionInstanceId, drawingId); return true; }
            catch (DrawingAnswerException) { return false; }
        }
        var outcomes = await Task.WhenAll(Vote(), Vote()); Assert.Single(outcomes, value => value);
        Assert.Equal(1, (await harness.DrawingCountsAsync(room.RoomCode)).Votes);
    }

    [Fact]
    public async Task LastUploadAndTimeout_ProduceOneConsistentTransition()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer, eligibleCount: 2);
        var png = await PhotoAnswerTestHarness.DrawingAsync();
        Assert.True((await harness.UploadDrawingAsync(room, room.Players[0], png)).IsSuccessStatusCode);
        var operations = await Task.WhenAll(
            UploadWithoutThrow(harness, room, room.Players[1], png),
            ProcessWithoutThrow(harness, room, DateTimeOffset.UtcNow.AddDays(1)));
        Assert.All(operations, Assert.True);
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        Assert.NotEqual(GameStage.CollectingDrawingAnswers, await db.GameSessions.Select(session => session.Stage).SingleAsync());
        var submissions = await db.DrawingAnswerSubmissions.AsNoTracking().ToListAsync();
        Assert.InRange(submissions.Count, 1, 2);
        Assert.Equal(submissions.Count, submissions.Select(submission => submission.Id).Distinct().Count());
    }

    [Fact]
    public async Task UploadAndDisplayDisconnect_HaveNoPartialWrite()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer, eligibleCount: 2);
        var png = await PhotoAnswerTestHarness.DrawingAsync();
        await Task.WhenAll(
            UploadWithoutThrow(harness, room, room.Players[0], png),
            DisconnectWithoutThrow(harness, room.RoomCode));
        var counts = await harness.DrawingCountsAsync(room.RoomCode);
        Assert.InRange(counts.Submissions, 0, 1);
        Assert.Equal(counts.Submissions, counts.Assets);
        Assert.Equal(counts.Submissions * 2, harness.FinalPngCount());
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        Assert.Equal(GameStage.PausedForDisplay, await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().GameSessions.Select(session => session.Stage).SingleAsync());
    }

    [Fact]
    public async Task LastVoteAndTimeout_CreateOneResultAndOneLedgerSet()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await PrepareVotingRoom(harness);
        var answerIds = await DrawingIds(harness);
        await Vote(harness, room, room.Players[0], answerIds[0]);
        await Vote(harness, room, room.Players[1], answerIds[1]);
        await Task.WhenAll(
            VoteWithoutThrow(harness, room, room.Players[2], answerIds[0]),
            ProcessWithoutThrow(harness, room, DateTimeOffset.UtcNow.AddDays(1)));
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var stage = await db.GameSessions.Select(session => session.Stage).SingleAsync();
        Assert.True(stage is GameStage.ShowingDrawingAnswerResults or GameStage.RoundSummary);
        var ledger = await db.ScoreTransactions.AsNoTracking().ToListAsync();
        Assert.Equal(ledger.Count, ledger.Select(transaction => (transaction.QuestionInstanceId, transaction.PlayerId)).Distinct().Count());
    }

    [Fact]
    public async Task ScoringInvokedTwiceUnderRoomLock_IsIdempotent()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await PrepareVotingRoom(harness);
        var answers = await DrawingIds(harness);
        for (var index = 0; index < room.Players.Count; index++) await Vote(harness, room, room.Players[index], answers[0]);
        await Task.WhenAll(ScoreUnderLock(harness, room), ScoreUnderLock(harness, room));
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var ledger = await db.ScoreTransactions.AsNoTracking().ToListAsync();
        Assert.Equal(3, ledger.Count);
        Assert.Equal(ledger.Sum(transaction => transaction.Points), await db.Players.SumAsync(player => player.Score));
    }

    [Fact]
    public async Task RevealOrderAssignedByConcurrentTransitions_IsStableAndUnique()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        var png = await PhotoAnswerTestHarness.DrawingAsync();
        foreach (var player in room.Players) Assert.True((await harness.UploadDrawingAsync(room, player, png)).IsSuccessStatusCode);
        await Task.WhenAll(ProcessUnderLock(harness, room, DateTimeOffset.UtcNow.AddMinutes(1)), ProcessUnderLock(harness, room, DateTimeOffset.UtcNow.AddMinutes(1)));
        var first = await RevealOrders(harness);
        await Task.WhenAll(ProcessUnderLock(harness, room, DateTimeOffset.UtcNow), ProcessUnderLock(harness, room, DateTimeOffset.UtcNow));
        var second = await RevealOrders(harness);
        Assert.Equal(first, second);
        Assert.Equal(3, first.Distinct().Count());
        Assert.DoesNotContain(first, order => order is null);
    }

    private static async Task<PhotoRoomAccess> PrepareVotingRoom(PhotoAnswerTestHarness harness)
    {
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        var png = await PhotoAnswerTestHarness.DrawingAsync();
        foreach (var player in room.Players) Assert.True((await harness.UploadDrawingAsync(room, player, png)).IsSuccessStatusCode);
        await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(10));
        return room;
    }

    private static async Task<List<Guid>> DrawingIds(PhotoAnswerTestHarness harness)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().DrawingAnswerSubmissions.OrderBy(submission => submission.Id).Select(submission => submission.Id).ToListAsync();
    }

    private static async Task<List<int?>> RevealOrders(PhotoAnswerTestHarness harness)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().DrawingAnswerSubmissions.OrderBy(submission => submission.Id).Select(submission => submission.RevealOrder).ToListAsync();
    }

    private static async Task Vote(PhotoAnswerTestHarness harness, PhotoRoomAccess room, PhotoPlayerAccess player, Guid answerId)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRoomService>().SubmitDrawingAnswerVoteAsync(room.RoomCode, player.PlayerId, player.Token, room.QuestionInstanceId, answerId);
    }

    private static async Task<bool> UploadWithoutThrow(PhotoAnswerTestHarness harness, PhotoRoomAccess room, PhotoPlayerAccess player, byte[] png)
    {
        try { _ = await harness.UploadDrawingAsync(room, player, png); return true; }
        catch { return false; }
    }

    private static async Task<bool> VoteWithoutThrow(PhotoAnswerTestHarness harness, PhotoRoomAccess room, PhotoPlayerAccess player, Guid answerId)
    {
        try { await Vote(harness, room, player, answerId); return true; }
        catch (DrawingAnswerException) { return true; }
    }

    private static async Task<bool> ProcessWithoutThrow(PhotoAnswerTestHarness harness, PhotoRoomAccess room, DateTimeOffset now)
    {
        try { _ = await harness.ProcessAtAsync(room, now); return true; }
        catch (DbUpdateConcurrencyException) { return true; }
    }

    private static async Task DisconnectWithoutThrow(PhotoAnswerTestHarness harness, string roomCode)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRoomService>().DisconnectDisplayAsync(roomCode);
    }

    private static async Task ProcessUnderLock(PhotoAnswerTestHarness harness, PhotoRoomAccess room, DateTimeOffset now)
    {
        var roomLock = harness.Factory.Services.GetRequiredService<RoomLockProvider>().For(room.RoomCode);
        await roomLock.WaitAsync();
        try { _ = await harness.ProcessAtUnderLockAsync(room, now); }
        finally { roomLock.Release(); }
    }

    private static async Task ScoreUnderLock(PhotoAnswerTestHarness harness, PhotoRoomAccess room)
    {
        var roomLock = harness.Factory.Services.GetRequiredService<RoomLockProvider>().For(room.RoomCode);
        await roomLock.WaitAsync();
        try
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.Include(game => game.Room).SingleAsync(game => game.Id == room.GameSessionId);
            await scope.ServiceProvider.GetRequiredService<ScoreCalculator>().CalculateAndApplyDrawingAnswerScoresAsync(session, DateTimeOffset.UtcNow, CancellationToken.None);
        }
        finally { roomLock.Release(); }
    }
}
