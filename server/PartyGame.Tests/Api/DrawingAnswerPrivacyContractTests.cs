using System.Text.Json;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;

namespace PartyGame.Tests.Api;

public sealed class DrawingAnswerPrivacyContractTests
{
    [Fact]
    public async Task Collecting_ContainsOnlyProgressAndNoDrawingDetails()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        Assert.True((await harness.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync())).IsSuccessStatusCode);
        var game = await GameJson(harness, room.RoomCode);
        Assert.True(game.TryGetProperty("submittedDrawingAnswers", out _));
        Assert.True(game.TryGetProperty("requiredDrawingAnswers", out _));
        Assert.True(game.TryGetProperty("submittedDrawingAnswerPlayerIds", out _));
        AssertForbidden(game, "drawingAnswerId", "displayDrawingUrl", "thumbnailDrawingUrl", "authorPlayerId", "ownDrawingAnswerId", "mediaAssetId", "storageKey", "revealOrder");
    }

    [Fact]
    public async Task Reveal_IsAnonymousAndExposesRevealContractOnly()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await RevealRoom(harness);
        var game = await GameJson(harness, room.RoomCode);
        var option = game.GetProperty("drawingAnswerResults").GetProperty("anonymousOptions")[0];
        AssertRequired(option, "drawingAnswerId", "displayDrawingUrl", "thumbnailDrawingUrl", "revealOrder", "width", "height");
        AssertForbidden(game, "authorPlayerId", "authorNickname", "authorPhotoUrl", "voteCount", "voters", "pointsAwarded", "mediaAssetId", "storageKey");
    }

    [Fact]
    public async Task Voting_RemainsAnonymousWithoutPartialResults()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await RevealRoom(harness);
        await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(10));
        var game = await GameJson(harness, room.RoomCode);
        var option = game.GetProperty("drawingAnswerResults").GetProperty("anonymousOptions")[0];
        Assert.True(option.TryGetProperty("displayOrder", out _));
        AssertForbidden(game, "authorPlayerId", "authorNickname", "voteCount", "voters", "pointsAwarded", "ownDrawingAnswerId", "mediaAssetId", "storageKey");
    }

    [Fact]
    public async Task Results_RevealAuthorsAndVotersOnlyAtResults()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await RevealRoom(harness);
        await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(10));
        await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow.AddMinutes(20));
        var game = await GameJson(harness, room.RoomCode);
        var option = game.GetProperty("drawingAnswerResults").GetProperty("options")[0];
        AssertRequired(option, "authorPlayerId", "authorNickname", "voteCount", "voters");
        AssertForbidden(game, "mediaAssetId", "storageKey", "ownDrawingAnswerId");
    }

    private static async Task<PhotoRoomAccess> RevealRoom(PhotoAnswerTestHarness harness)
    {
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        var png = await PhotoAnswerTestHarness.DrawingAsync();
        foreach (var player in room.Players) Assert.True((await harness.UploadDrawingAsync(room, player, png)).IsSuccessStatusCode);
        return room;
    }

    private static async Task<JsonElement> GameJson(PhotoAnswerTestHarness harness, string roomCode)
    {
        var json = await harness.Client.GetStringAsync($"/api/rooms/{roomCode}");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("game").Clone();
    }

    private static void AssertForbidden(JsonElement element, params string[] names)
    {
        var json = element.GetRawText();
        foreach (var name in names) Assert.DoesNotContain($"\"{name}\"", json, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertRequired(JsonElement element, params string[] names)
    {
        foreach (var name in names) Assert.True(element.TryGetProperty(name, out _), $"Missing property {name}");
    }
}
