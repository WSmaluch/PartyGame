using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class PhotoAnswerRestartPersistenceTests
{
    [Theory]
    [InlineData(GameStage.CollectingPhotoAnswers)]
    [InlineData(GameStage.RevealingPhotoAnswers)]
    [InlineData(GameStage.CollectingPhotoAnswerVotes)]
    [InlineData(GameStage.ShowingPhotoAnswerResults)]
    public async Task RealHostRestart_PreservesPhotoGameStateAndMedia(GameStage restartStage)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.Restart.Tests", Guid.NewGuid().ToString("N"));
        PhotoRoomAccess room;
        Guid assetId;
        Guid answerId;
        await using (var first = new PhotoAnswerTestHarness(directory, deleteOnDispose: false))
        {
            room = await first.CreateRoomAsync(eligibleCount: 2);
            await first.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync());
            await using var scope = first.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var submission = await db.PhotoAnswerSubmissions.SingleAsync();
            submission.RevealOrder = restartStage == GameStage.CollectingPhotoAnswers ? null : 0;
            answerId = submission.Id;
            assetId = submission.MediaAssetId;
            var session = await db.GameSessions.SingleAsync(candidate => candidate.Id == room.GameSessionId);
            session.Stage = restartStage;
            var instance = await db.GameQuestionInstances.SingleAsync(candidate => candidate.Id == room.QuestionInstanceId);
            instance.Stage = restartStage;
            if (restartStage == GameStage.CollectingPhotoAnswerVotes)
            {
                db.PhotoAnswerVotes.Add(new PhotoAnswerVote
                {
                    Id = Guid.NewGuid(),
                    QuestionInstanceId = room.QuestionInstanceId,
                    VoterPlayerId = room.Players[1].PlayerId,
                    SelectedPhotoAnswerId = answerId,
                    SubmittedAtUtc = DateTimeOffset.UtcNow
                });
            }
            await db.SaveChangesAsync();
        }

        await using var second = new PhotoAnswerTestHarness(directory);
        await using (var scope = second.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var persistedRoom = await db.GameRooms.AsNoTracking().SingleAsync(candidate => candidate.Code == room.RoomCode);
            var session = await db.GameSessions.AsNoTracking().SingleAsync(candidate => candidate.Id == room.GameSessionId);
            var submission = await db.PhotoAnswerSubmissions.AsNoTracking().SingleAsync(candidate => candidate.Id == answerId);
            Assert.Equal(room.RoomId, persistedRoom.Id);
            Assert.Equal(room.QuestionInstanceId, session.CurrentQuestionInstanceId);
            Assert.Equal(restartStage, session.Stage);
            Assert.Equal(restartStage == GameStage.CollectingPhotoAnswers ? null : 0, submission.RevealOrder);
            Assert.Equal(2, await db.PhotoAnswerEligiblePlayers.CountAsync());
            Assert.Equal(restartStage == GameStage.CollectingPhotoAnswerVotes ? 1 : 0, await db.PhotoAnswerVotes.CountAsync());
            Assert.Equal(0, await db.ScoreTransactions.CountAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await second.Client.GetAsync($"/api/media/{assetId}/display")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.Client.GetAsync($"/api/media/{assetId}/thumbnail")).StatusCode);
        var privateState = await ResumeAsync(second, room, room.Players[0]);
        Assert.Contains(answerId.ToString(), privateState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingDisplayFile_Returns404WhileRoomAndWorkerRemainHealthy()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync());
        Guid assetId;
        string displayKey;
        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var asset = await db.MediaAssets.AsNoTracking().SingleAsync();
            assetId = asset.Id;
            displayKey = asset.DisplayStorageKey;
        }
        File.Delete(Path.Combine(harness.Factory.MediaRootPath, displayKey.Replace('/', Path.DirectorySeparatorChar)));
        Assert.Equal(HttpStatusCode.NotFound, (await harness.Client.GetAsync($"/api/media/{assetId}/display")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await harness.Client.GetAsync($"/api/rooms/{room.RoomCode}")).StatusCode);
        await using var workerScope = harness.Factory.Services.CreateAsyncScope();
        var machine = workerScope.ServiceProvider.GetRequiredService<PartyGame.Infrastructure.Rooms.GameStateMachine>();
        await machine.ProcessTransitionAsync(room.GameSessionId, DateTimeOffset.UtcNow, CancellationToken.None);
    }

    private static async Task<string> ResumeAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room, PhotoPlayerAccess player)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{room.RoomCode}/players/{player.PlayerId}/resume");
        request.Headers.Add("X-Player-Token", player.Token);
        return await (await harness.Client.SendAsync(request)).Content.ReadAsStringAsync();
    }
}
