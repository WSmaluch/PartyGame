using System.Text.Json;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;

namespace PartyGame.Tests.Api;

public sealed class DrawingAnswerSingleSubmissionTests
{
    [Fact]
    public async Task OneDrawing_RevealsAnonymouslySkipsVotingAndContinues()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer, eligibleCount: 1);
        Assert.True((await harness.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync())).IsSuccessStatusCode);
        var revealJson = await harness.Client.GetStringAsync($"/api/rooms/{room.RoomCode}");
        Assert.Contains("displayDrawingUrl", revealJson); Assert.DoesNotContain("authorNickname", revealJson);
        using (var reveal = JsonDocument.Parse(revealJson))
        {
            var mediaUrl = reveal.RootElement.GetProperty("game").GetProperty("drawingAnswerResults")
                .GetProperty("anonymousOptions")[0].GetProperty("displayDrawingUrl").GetString();
            using var mediaResponse = await harness.Client.GetAsync(mediaUrl);
            Assert.Equal(System.Net.HttpStatusCode.OK, mediaResponse.StatusCode);
            Assert.Equal("image/png", mediaResponse.Content.Headers.ContentType?.MediaType);
        }
        Assert.Equal(GameStage.ShowingDrawingAnswerResults, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(1)));
        var resultsJson = await harness.Client.GetStringAsync($"/api/rooms/{room.RoomCode}"); using var document = JsonDocument.Parse(resultsJson);
        var results = document.RootElement.GetProperty("game").GetProperty("drawingAnswerResults");
        Assert.Single(results.GetProperty("options").EnumerateArray()); Assert.Equal(room.Players[0].Nickname, results.GetProperty("options")[0].GetProperty("authorNickname").GetString());
        var counts = await harness.DrawingCountsAsync(room.RoomCode); Assert.Equal((0, 0), (counts.Votes, counts.Scores));
        Assert.Equal(GameStage.RoundSummary, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(2)));
    }
}
