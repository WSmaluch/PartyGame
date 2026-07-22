using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class PhotoAnswerDisplayPauseTests
{
    [Theory]
    [InlineData(GameStage.CollectingPhotoAnswers)]
    [InlineData(GameStage.RevealingPhotoAnswers)]
    [InlineData(GameStage.CollectingPhotoAnswerVotes)]
    [InlineData(GameStage.ShowingPhotoAnswerResults)]
    public async Task DisconnectAndReconnect_PreservesEveryPhotoStage(GameStage stage)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(stage: stage);
        var before = await ReadAsync(harness, room);

        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRoomService>();
            await service.DisconnectDisplayAsync(room.RoomCode);
        }
        var paused = await ReadAsync(harness, room);
        Assert.Equal(GameStage.PausedForDisplay, paused.Stage);
        Assert.Equal(stage, paused.PausedStage);
        Assert.InRange(paused.Remaining!.Value, 295_000, 305_000);

        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var machine = scope.ServiceProvider.GetRequiredService<GameStateMachine>();
            Assert.False(await machine.ProcessTransitionAsync(room.GameSessionId, DateTimeOffset.UtcNow.AddDays(1), CancellationToken.None));
        }
        Assert.Equal(GameStage.PausedForDisplay, (await ReadAsync(harness, room)).Stage);

        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRoomService>();
            await service.AttachDisplayAsync(room.RoomCode);
        }
        var resumed = await ReadAsync(harness, room);
        Assert.Equal(stage, resumed.Stage);
        Assert.Null(resumed.PausedStage);
        Assert.InRange((resumed.EndsAt!.Value - DateTimeOffset.UtcNow).TotalMilliseconds, 294_000, 305_000);
        Assert.Equal(before.RevealOrders, resumed.RevealOrders);
        Assert.Equal(before.Submissions, resumed.Submissions);
        Assert.Equal(before.Votes, resumed.Votes);
        Assert.Equal(before.Scores, resumed.Scores);
    }

    [Fact]
    public async Task UploadDuringPause_IsAtomicRejection()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(stage: GameStage.PausedForDisplay);
        var before = await harness.CountsAsync(room.RoomCode);
        var response = await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync());
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(before, await harness.CountsAsync(room.RoomCode));
        Assert.Equal(0, harness.FinalJpegCount());
    }

    [Fact]
    public async Task VoteDuringPause_IsRejectedWithoutMutation()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(stage: GameStage.PausedForDisplay);
        var before = await harness.CountsAsync(room.RoomCode);
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IRoomService>();
        var exception = await Assert.ThrowsAsync<PhotoAnswerException>(() => service.SubmitPhotoAnswerVoteAsync(
            room.RoomCode, room.Players[0].PlayerId, room.Players[0].Token, room.QuestionInstanceId, Guid.NewGuid()));
        Assert.Equal("photo_answer_vote_not_active", exception.Code);
        Assert.Equal(before, await harness.CountsAsync(room.RoomCode));
    }

    private static async Task<PauseState> ReadAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var session = await db.GameSessions.AsNoTracking().SingleAsync(candidate => candidate.Id == room.GameSessionId);
        return new PauseState(
            session.Stage,
            session.PausedStage,
            session.PausedRemainingMilliseconds,
            session.StageEndsAtUtc,
            await db.PhotoAnswerSubmissions.CountAsync(),
            await db.PhotoAnswerVotes.CountAsync(),
            await db.ScoreTransactions.CountAsync(),
            await db.PhotoAnswerSubmissions.OrderBy(candidate => candidate.Id).Select(candidate => candidate.RevealOrder).ToListAsync());
    }

    private sealed record PauseState(GameStage Stage, GameStage? PausedStage, double? Remaining, DateTimeOffset? EndsAt, int Submissions, int Votes, int Scores, List<int?> RevealOrders);
}
