using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class DrawingAnswerRestartPersistenceTests
{
    [Theory]
    [InlineData(GameStage.CollectingDrawingAnswers)]
    [InlineData(GameStage.RevealingDrawingAnswers)]
    [InlineData(GameStage.CollectingDrawingAnswerVotes)]
    [InlineData(GameStage.ShowingDrawingAnswerResults)]
    public async Task RealHostRestart_PreservesDrawingStateMediaAndPrivateState(GameStage restartStage)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.DrawingRestart", Guid.NewGuid().ToString("N"));
        PhotoRoomAccess room; Guid answerId; Guid assetId; int? revealOrder;
        await using (var first = new PhotoAnswerTestHarness(directory, deleteOnDispose: false))
        {
            room = await first.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
            Assert.True((await first.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync())).IsSuccessStatusCode);
            await using var scope = first.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>(); var submission = await db.DrawingAnswerSubmissions.SingleAsync(); answerId = submission.Id; assetId = submission.MediaAssetId;
            if (restartStage != GameStage.CollectingDrawingAnswers) submission.RevealOrder = 0;
            var session = await db.GameSessions.SingleAsync(s => s.Id == room.GameSessionId); session.Stage = restartStage; session.StageEndsAtUtc = DateTimeOffset.UtcNow.AddMinutes(5); var instance = await db.GameQuestionInstances.SingleAsync(i => i.Id == room.QuestionInstanceId); instance.Stage = restartStage; await db.SaveChangesAsync(); revealOrder = submission.RevealOrder;
        }
        await using (var second = new PhotoAnswerTestHarness(directory, deleteOnDispose: false))
        {
            Assert.True((await second.Client.GetAsync($"/api/rooms/{room.RoomCode}")).IsSuccessStatusCode);
            Assert.True((await second.Client.GetAsync($"/api/media/{assetId}/display")).IsSuccessStatusCode);
            Assert.True((await second.Client.GetAsync($"/api/media/{assetId}/thumbnail")).IsSuccessStatusCode);
            await using var scope = second.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            Assert.Equal(restartStage, await db.GameSessions.Where(s => s.Id == room.GameSessionId).Select(s => s.Stage).SingleAsync());
            Assert.Equal(revealOrder, await db.DrawingAnswerSubmissions.Where(s => s.Id == answerId).Select(s => s.RevealOrder).SingleAsync());
            var state = await scope.ServiceProvider.GetRequiredService<IRoomService>().GetPlayerPrivateGameStateAsync(room.RoomCode, room.Players[0].PlayerId); Assert.True(state.HasSubmittedDrawingAnswer); Assert.Equal(answerId, state.OwnDrawingAnswerId);
        }
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Fact]
    public async Task MissingDisplayFile_Returns404WhileRoomSnapshotStillWorks()
    {
        await using var harness = new PhotoAnswerTestHarness(); var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer); Assert.True((await harness.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync())).IsSuccessStatusCode);
        Guid assetId; string displayKey; await using (var scope = harness.Factory.Services.CreateAsyncScope()) { var asset = await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().MediaAssets.SingleAsync(); assetId = asset.Id; displayKey = asset.DisplayStorageKey; }
        File.Delete(Path.Combine(harness.Factory.MediaRootPath, displayKey));
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await harness.Client.GetAsync($"/api/media/{assetId}/display")).StatusCode);
        Assert.True((await harness.Client.GetAsync($"/api/rooms/{room.RoomCode}")).IsSuccessStatusCode);
    }
}
