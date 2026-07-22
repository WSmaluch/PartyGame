using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class PhotoAnswerConcurrencyTests
{
    [Fact]
    public async Task SameClientSubmissionId_ConcurrentUploadsAreIdempotent()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        var image = await PhotoAnswerTestHarness.ImageAsync();
        var clientId = Guid.NewGuid();
        var before = await harness.CountsAsync(room.RoomCode);

        var responses = await Task.WhenAll(
            harness.UploadAsync(room, room.Players[0], image, clientSubmissionId: clientId),
            harness.UploadAsync(room, room.Players[0], image, clientSubmissionId: clientId));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var ids = await Task.WhenAll(responses.Select(async response => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("photoAnswerId").GetGuid()));
        Assert.Equal(ids[0], ids[1]);
        var after = await harness.CountsAsync(room.RoomCode);
        Assert.Equal((1, 1, before.Version + 1, 2), (after.Submissions, after.Assets, after.Version, harness.FinalJpegCount()));
    }

    [Fact]
    public async Task DifferentClientSubmissionIds_ExactlyOneConcurrentUploadWins()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        var image = await PhotoAnswerTestHarness.ImageAsync();
        var responses = await Task.WhenAll(
            harness.UploadAsync(room, room.Players[0], image, clientSubmissionId: Guid.NewGuid()),
            harness.UploadAsync(room, room.Players[0], image, clientSubmissionId: Guid.NewGuid()));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var counts = await harness.CountsAsync(room.RoomCode);
        Assert.Equal((1, 1, 2), (counts.Submissions, counts.Assets, harness.FinalJpegCount()));
    }

    [Fact]
    public async Task LastUploadAndTimeout_ProduceOneConsistentTransition()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        var image = await PhotoAnswerTestHarness.ImageAsync();
        await harness.UploadAsync(room, room.Players[0], image);
        await Task.WhenAll(
            harness.UploadAsync(room, room.Players[1], image),
            ProcessLockedAsync(harness, room, DateTimeOffset.UtcNow.AddDays(1)));

        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var session = await db.GameSessions.AsNoTracking().SingleAsync(candidate => candidate.Id == room.GameSessionId);
        Assert.True(session.Stage is GameStage.RevealingPhotoAnswers or GameStage.CollectingPhotoAnswerVotes);
        var submissions = await db.PhotoAnswerSubmissions.AsNoTracking().OrderBy(candidate => candidate.RevealOrder).ToListAsync();
        Assert.InRange(submissions.Count, 1, 2);
        Assert.Equal(submissions.Count, submissions.Select(candidate => candidate.RevealOrder).Distinct().Count());
    }

    [Fact]
    public async Task UploadAndDisplayDisconnect_HaveNoPartialWrite()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        var uploadTask = harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync());
        var disconnectTask = DisconnectAsync(harness, room.RoomCode);
        await Task.WhenAll(uploadTask, disconnectTask);
        var counts = await harness.CountsAsync(room.RoomCode);
        Assert.True((counts.Submissions, counts.Assets, harness.FinalJpegCount()) is (1, 1, 2) or (0, 0, 0));
    }

    [Fact]
    public async Task TwoVotesByOnePlayer_OneWinsAndOneConflicts()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await PrepareVotingRoomAsync(harness);
        var answerIds = await AnswerIdsAsync(harness);
        var results = await Task.WhenAll(
            VoteAsync(harness, room, room.Players[2], answerIds[0]),
            VoteAsync(harness, room, room.Players[2], answerIds[1]));
        Assert.Single(results, result => result is null);
        Assert.Single(results, result => result?.Code == "photo_answer_vote_already_submitted");
        Assert.Equal(1, (await harness.CountsAsync(room.RoomCode)).Votes);
    }

    [Fact]
    public async Task RevealOrderAssignedByConcurrentTransitions_IsStableAndUnique()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 3);
        var image = await PhotoAnswerTestHarness.ImageAsync();
        await harness.UploadAsync(room, room.Players[0], image);
        await harness.UploadAsync(room, room.Players[1], image);
        await Task.WhenAll(
            ProcessLockedAsync(harness, room, DateTimeOffset.UtcNow.AddDays(1)),
            ProcessLockedAsync(harness, room, DateTimeOffset.UtcNow.AddDays(1)));
        var first = await RevealOrdersAsync(harness);
        var second = await RevealOrdersAsync(harness);
        Assert.Equal(first, second);
        Assert.Equal(first.Count, first.Distinct().Count());
        Assert.DoesNotContain(first, value => value is null);
    }

    [Fact]
    public async Task CalculatorInvokedTwiceUnderRoomLock_IsIdempotent()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await PrepareVotingRoomAsync(harness);
        var answerIds = await AnswerIdsAsync(harness);
        Assert.Null(await VoteAsync(harness, room, room.Players[2], answerIds[0]));
        await Task.WhenAll(CalculateLockedAsync(harness, room), CalculateLockedAsync(harness, room));
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        Assert.Equal(1, await db.ScoreTransactions.CountAsync());
        Assert.Equal((await db.ScoreTransactions.SingleAsync()).Points, await db.Players.Where(player => player.Id == room.Players[2].PlayerId).Select(player => player.Score).SingleAsync());
    }

    [Fact]
    public async Task LastVoteAndTimeout_CreateOneResultAndOneLedgerSet()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await PrepareVotingRoomAsync(harness);
        var answerIds = await AnswerIdsAsync(harness);
        Assert.Null(await VoteAsync(harness, room, room.Players[0], answerIds[0]));
        Assert.Null(await VoteAsync(harness, room, room.Players[1], answerIds[1]));

        await Task.WhenAll(
            VoteAsync(harness, room, room.Players[2], answerIds[0]),
            ForceVoteTimeoutLockedAsync(harness, room));

        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        Assert.Equal(GameStage.ShowingPhotoAnswerResults, await db.GameSessions.Select(session => session.Stage).SingleAsync());
        var ledger = await db.ScoreTransactions.AsNoTracking().ToListAsync();
        Assert.Equal(ledger.Count, ledger.Select(transaction => (transaction.QuestionInstanceId, transaction.PlayerId)).Distinct().Count());
        foreach (var player in room.Players)
        {
            var expected = ledger.Where(transaction => transaction.PlayerId == player.PlayerId).Sum(transaction => transaction.Points);
            Assert.Equal(expected, await db.Players.Where(candidate => candidate.Id == player.PlayerId).Select(candidate => candidate.Score).SingleAsync());
        }
    }

    private static async Task<PhotoRoomAccess> PrepareVotingRoomAsync(PhotoAnswerTestHarness harness)
    {
        var room = await harness.CreateRoomAsync(eligibleCount: 3);
        var image = await PhotoAnswerTestHarness.ImageAsync();
        await harness.UploadAsync(room, room.Players[0], image);
        await harness.UploadAsync(room, room.Players[1], image);
        var roomLock = harness.Factory.Services.GetRequiredService<RoomLockProvider>().For(room.RoomCode);
        await roomLock.WaitAsync();
        try
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var submissionIds = await db.PhotoAnswerSubmissions
                .Where(candidate => candidate.QuestionInstanceId == room.QuestionInstanceId)
                .OrderBy(candidate => candidate.Id)
                .Select(candidate => candidate.Id)
                .ToListAsync();
            for (var index = 0; index < submissionIds.Count; index++)
            {
                var revealOrder = index;
                await db.PhotoAnswerSubmissions
                    .Where(candidate => candidate.Id == submissionIds[index])
                    .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.RevealOrder, revealOrder));
            }

            await db.GameSessions
                .Where(candidate => candidate.Id == room.GameSessionId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(candidate => candidate.Stage, GameStage.CollectingPhotoAnswerVotes)
                    .SetProperty(candidate => candidate.StageEndsAtUtc, DateTimeOffset.UtcNow.AddMinutes(5)));
            await db.GameQuestionInstances
                .Where(candidate => candidate.Id == room.QuestionInstanceId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(candidate => candidate.Stage, GameStage.CollectingPhotoAnswerVotes));

            if (!await db.PhotoAnswerVoteEligiblePlayers.AnyAsync(candidate => candidate.QuestionInstanceId == room.QuestionInstanceId))
            {
                db.PhotoAnswerVoteEligiblePlayers.AddRange(room.Players.Select(player => new PhotoAnswerVoteEligiblePlayer
                {
                    Id = Guid.NewGuid(),
                    QuestionInstanceId = room.QuestionInstanceId,
                    PlayerId = player.PlayerId,
                }));
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            roomLock.Release();
        }
        return room;
    }

    private static async Task ProcessLockedAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room, DateTimeOffset now)
    {
        var roomLock = harness.Factory.Services.GetRequiredService<RoomLockProvider>().For(room.RoomCode);
        await roomLock.WaitAsync();
        try
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope();
            var machine = scope.ServiceProvider.GetRequiredService<GameStateMachine>();
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            if (await machine.ProcessTransitionAsync(room.GameSessionId, now, CancellationToken.None))
            {
                var trackedRoom = await db.GameRooms.SingleAsync(candidate => candidate.Id == room.RoomId);
                trackedRoom.PublicStateChanged(now);
                await db.SaveChangesAsync();
            }
        }
        finally { roomLock.Release(); }
    }

    private static async Task CalculateLockedAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room)
    {
        var roomLock = harness.Factory.Services.GetRequiredService<RoomLockProvider>().For(room.RoomCode);
        await roomLock.WaitAsync();
        try
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var calculator = scope.ServiceProvider.GetRequiredService<ScoreCalculator>();
            var session = await db.GameSessions.Include(candidate => candidate.Room).ThenInclude(candidate => candidate.Players).Include(candidate => candidate.Rounds).ThenInclude(candidate => candidate.Questions).SingleAsync(candidate => candidate.Id == room.GameSessionId);
            await calculator.CalculateAndApplyPhotoAnswerScoresAsync(session, DateTimeOffset.UtcNow, CancellationToken.None);
            await db.SaveChangesAsync();
        }
        finally { roomLock.Release(); }
    }

    private static async Task ForceVoteTimeoutLockedAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room)
    {
        var roomLock = harness.Factory.Services.GetRequiredService<RoomLockProvider>().For(room.RoomCode);
        await roomLock.WaitAsync();
        try
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions
                .Include(candidate => candidate.Room).ThenInclude(candidate => candidate.Players)
                .Include(candidate => candidate.Rounds).ThenInclude(candidate => candidate.Questions)
                .SingleAsync(candidate => candidate.Id == room.GameSessionId);
            if (session.Stage == GameStage.CollectingPhotoAnswerVotes)
            {
                await scope.ServiceProvider.GetRequiredService<GameStateMachine>().ForceTransitionAsync(session, DateTimeOffset.UtcNow, CancellationToken.None);
                session.Room.PublicStateChanged(DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
            }
        }
        finally { roomLock.Release(); }
    }

    private static async Task DisconnectAsync(PhotoAnswerTestHarness harness, string roomCode)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IRoomService>().DisconnectDisplayAsync(roomCode);
    }

    private static async Task<PhotoAnswerException?> VoteAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room, PhotoPlayerAccess player, Guid answerId)
    {
        try
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IRoomService>().SubmitPhotoAnswerVoteAsync(room.RoomCode, player.PlayerId, player.Token, room.QuestionInstanceId, answerId);
            return null;
        }
        catch (PhotoAnswerException exception) { return exception; }
    }

    private static async Task<List<Guid>> AnswerIdsAsync(PhotoAnswerTestHarness harness)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().PhotoAnswerSubmissions.AsNoTracking().Select(candidate => candidate.Id).ToListAsync();
    }

    private static async Task<List<int?>> RevealOrdersAsync(PhotoAnswerTestHarness harness)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().PhotoAnswerSubmissions.AsNoTracking().OrderBy(candidate => candidate.Id).Select(candidate => candidate.RevealOrder).ToListAsync();
    }
}
