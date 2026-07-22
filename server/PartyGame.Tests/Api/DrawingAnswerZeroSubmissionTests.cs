using System.Text.Json;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;

namespace PartyGame.Tests.Api;

public sealed class DrawingAnswerZeroSubmissionTests
{
    [Fact]
    public async Task ZeroDrawings_SkipsRevealAndVotingAndContinues()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer, stageEndsAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        Assert.Equal(GameStage.ShowingDrawingAnswerResults, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow));
        var json = await harness.Client.GetStringAsync($"/api/rooms/{room.RoomCode}"); using var document = JsonDocument.Parse(json);
        Assert.Equal(0, document.RootElement.GetProperty("game").GetProperty("drawingAnswerResults").GetProperty("options").GetArrayLength());
        var counts = await harness.DrawingCountsAsync(room.RoomCode); Assert.Equal((0, 0, 0), (counts.Submissions, counts.Votes, counts.Scores));
        Assert.Equal(GameStage.RoundSummary, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(1)));
    }
}
