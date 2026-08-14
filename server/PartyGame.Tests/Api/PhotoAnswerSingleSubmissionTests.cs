using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class PhotoAnswerSingleSubmissionTests
{
    [Fact]
    public async Task OnePhoto_RevealsAnonymouslySkipsVotingAndContinues()
    {
        await using var harness = new PhotoAnswerTestHarness(settings: new Dictionary<string, string?>
        {
            ["GameFlow:PhotoAnswerRevealBaseSeconds"] = "0",
            ["GameFlow:PhotoAnswerRevealPerPhotoSeconds"] = "1",
            ["GameFlow:PhotoAnswerResultsSeconds"] = "1"
        });
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        var upload = await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync());
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.SingleAsync(candidate => candidate.Id == room.GameSessionId);
            session.StageEndsAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(GameStage.RevealingPhotoAnswers, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow));

        var revealJson = await harness.Client.GetStringAsync($"/api/rooms/{room.RoomCode}");
        var reveal = JsonDocument.Parse(revealJson).RootElement.GetProperty("game").GetProperty("photoAnswerResults");
        var option = Assert.Single(reveal.GetProperty("anonymousOptions").EnumerateArray());
        Assert.True(option.TryGetProperty("displayPhotoUrl", out var displayUrl));
        Assert.True(option.TryGetProperty("thumbnailPhotoUrl", out var thumbnailUrl));
        Assert.False(option.TryGetProperty("authorPlayerId", out _));
        using var displayResponse = await harness.Client.GetAsync(displayUrl.GetString());
        using var thumbnailResponse = await harness.Client.GetAsync(thumbnailUrl.GetString());
        Assert.Equal(HttpStatusCode.OK, displayResponse.StatusCode);
        Assert.Equal("image/jpeg", displayResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, thumbnailResponse.StatusCode);
        Assert.Equal("image/jpeg", thumbnailResponse.Content.Headers.ContentType?.MediaType);

        Assert.Equal(GameStage.ShowingPhotoAnswerResults, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddSeconds(2)));
        var resultJson = await harness.Client.GetStringAsync($"/api/rooms/{room.RoomCode}");
        Assert.Contains(room.Players[0].PlayerId.ToString(), resultJson, StringComparison.OrdinalIgnoreCase);
        var counts = await harness.CountsAsync(room.RoomCode);
        Assert.Equal((0, 0), (counts.Votes, counts.Scores));

        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            Assert.All(await db.Players.AsNoTracking().ToListAsync(), player => Assert.Equal(0, player.Score));
        }
        Assert.Equal(GameStage.RoundSummary, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddSeconds(4)));
    }

    [Fact]
    public async Task OwnPhotoAnswerId_IsPrivateAndRecovered()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        var upload = await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync());
        var uploadJson = await upload.Content.ReadAsStringAsync();
        var photoId = JsonDocument.Parse(uploadJson).RootElement.GetProperty("photoAnswerId").GetGuid();
        Assert.Contains(photoId.ToString(), uploadJson, StringComparison.OrdinalIgnoreCase);
        var publicJson = await harness.Client.GetStringAsync($"/api/rooms/{room.RoomCode}");
        Assert.DoesNotContain(photoId.ToString(), publicJson, StringComparison.OrdinalIgnoreCase);
    }
}
