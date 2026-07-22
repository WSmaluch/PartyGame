using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class PhotoAnswerZeroSubmissionTests
{
    [Fact]
    public async Task Timeout_WithNoPhotos_SkipsRevealAndVotingAndContinuesGame()
    {
        await using var harness = new PhotoAnswerTestHarness(settings: new Dictionary<string, string?> { ["GameFlow:PhotoAnswerResultsSeconds"] = "1" });
        var expired = DateTimeOffset.UtcNow.AddSeconds(-1);
        var room = await harness.CreateRoomAsync(stageEndsAtUtc: expired);

        Assert.Equal(GameStage.ShowingPhotoAnswerResults, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow));
        var counts = await harness.CountsAsync(room.RoomCode);
        Assert.Equal((0, 0, 0, 0), (counts.Submissions, counts.Assets, counts.Votes, counts.Scores));

        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.AsNoTracking().SingleAsync(candidate => candidate.Id == room.GameSessionId);
            Assert.InRange((session.StageEndsAtUtc!.Value - session.StageStartedAtUtc).TotalSeconds, 0.9, 1.1);
        }

        Assert.Equal(GameStage.RoundSummary, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddSeconds(2)));
        Assert.Equal(GameStage.GameSummary, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddSeconds(10)));
        Assert.Equal(GameStage.Completed, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddSeconds(30)));
    }

    [Fact]
    public async Task PublicResults_WithNoPhotos_AreEmpty()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(stageEndsAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow);
        var json = await harness.Client.GetStringAsync($"/api/rooms/{room.RoomCode}");
        Assert.Contains("\"options\":[]", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"anonymousOptions\":null", json, StringComparison.OrdinalIgnoreCase);
    }
}
