using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class DrawingAnswerDisplayPauseTests
{
    [Theory]
    [InlineData(GameStage.CollectingDrawingAnswers)]
    [InlineData(GameStage.RevealingDrawingAnswers)]
    [InlineData(GameStage.CollectingDrawingAnswerVotes)]
    [InlineData(GameStage.ShowingDrawingAnswerResults)]
    public async Task DisconnectAndReconnect_PreservesEveryDrawingStage(GameStage stage)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(stage, QuestionType.DrawingAnswer, stageEndsAtUtc: DateTimeOffset.UtcNow.AddMinutes(2));
        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRoomService>();
            await service.DisconnectDisplayAsync(room.RoomCode);
        }
        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.SingleAsync(s => s.Id == room.GameSessionId);
            Assert.Equal(GameStage.PausedForDisplay, session.Stage); Assert.Equal(stage, session.PausedStage); Assert.True(session.PausedRemainingMilliseconds > 0);
        }
        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IRoomService>();
            await service.AttachDisplayAsync(room.RoomCode);
        }
        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var session = await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().GameSessions.SingleAsync(s => s.Id == room.GameSessionId);
            Assert.Equal(stage, session.Stage); Assert.Null(session.PausedStage); Assert.True(session.StageEndsAtUtc > DateTimeOffset.UtcNow);
        }
    }

    [Fact]
    public async Task UploadWhilePaused_IsRejectedWithoutRowsOrFiles()
    {
        await using var harness = new PhotoAnswerTestHarness(); var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        await using (var scope = harness.Factory.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<IRoomService>().DisconnectDisplayAsync(room.RoomCode);
        var response = await harness.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync());
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        var counts = await harness.DrawingCountsAsync(room.RoomCode); Assert.Equal((0, 0, 0), (counts.Submissions, counts.Assets, harness.FinalPngCount()));
    }
}
