using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using SixLabors.ImageSharp;

namespace PartyGame.Tests.Api;

public sealed class DrawingAnswerUploadEndpointTests
{
    [Fact]
    public async Task ValidPng_IsPersistedAsPngWithoutInternalKeys()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        var response = await harness.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("drawingAnswerId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mediaAssetId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(harness.Factory.MediaRootPath, json, StringComparison.Ordinal);
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var asset = await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().MediaAssets.SingleAsync();
        Assert.Equal("image/png", asset.ContentType);
        Assert.EndsWith(".png", asset.DisplayStorageKey);
        Assert.EndsWith(".png", asset.ThumbnailStorageKey);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("corrupt")]
    [InlineData("jpeg")]
    [InlineData("mime")]
    [InlineData("white")]
    [InlineData("transparent")]
    [InlineData("single-pixel")]
    public async Task InvalidPayloads_ReturnControlledBadRequest(string scenario)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        byte[]? bytes = scenario switch
        {
            "missing" => null,
            "empty" => [],
            "corrupt" => "not-png"u8.ToArray(),
            "jpeg" => await PhotoAnswerTestHarness.ImageAsync(),
            "white" => await PhotoAnswerTestHarness.DrawingAsync(drawLine: false),
            "transparent" => await PhotoAnswerTestHarness.DrawingAsync(transparent: true, drawLine: false),
            "single-pixel" => await SinglePixelPng(),
            _ => await PhotoAnswerTestHarness.DrawingAsync()
        };
        var contentType = scenario == "mime" ? "image/gif" : "image/png";
        var response = await harness.UploadDrawingAsync(room, room.Players[0], bytes, contentType);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal((0, 0), ((await harness.DrawingCountsAsync(room.RoomCode)).Submissions, harness.FinalPngCount()));
    }

    [Theory]
    [InlineData(319, 400, "DrawingMedia:MinimumWidth", "320")]
    [InlineData(400, 319, "DrawingMedia:MinimumHeight", "320")]
    [InlineData(501, 400, "DrawingMedia:MaximumWidth", "500")]
    [InlineData(400, 501, "DrawingMedia:MaximumHeight", "500")]
    public async Task DimensionLimits_ReturnBadRequest(int width, int height, string setting, string value)
    {
        await using var harness = new PhotoAnswerTestHarness(settings: new Dictionary<string, string?> { [setting] = value });
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        var response = await harness.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync(width, height));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ThinLine_IsAccepted()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        Assert.Equal(HttpStatusCode.OK, (await harness.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync())).StatusCode);
    }

    [Theory]
    [InlineData("room", HttpStatusCode.NotFound)]
    [InlineData("player", HttpStatusCode.NotFound)]
    [InlineData("token", HttpStatusCode.Unauthorized)]
    [InlineData("question", HttpStatusCode.Conflict)]
    [InlineData("type", HttpStatusCode.Conflict)]
    [InlineData("stage", HttpStatusCode.Conflict)]
    [InlineData("timeout", HttpStatusCode.Conflict)]
    [InlineData("ineligible", HttpStatusCode.Conflict)]
    public async Task RouteIdentityAndStateFailures_AreControlled(string scenario, HttpStatusCode expected)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var stage = scenario == "stage" ? GameStage.RevealingDrawingAnswers : GameStage.CollectingDrawingAnswers;
        var type = scenario == "type" ? QuestionType.PhotoAnswer : QuestionType.DrawingAnswer;
        var room = await harness.CreateRoomAsync(stage, type, eligibleCount: scenario == "ineligible" ? 0 : 3, stageEndsAtUtc: scenario == "timeout" ? DateTimeOffset.UtcNow.AddMinutes(-1) : null);
        var response = await harness.UploadDrawingAsync(room, room.Players[0], await PhotoAnswerTestHarness.DrawingAsync(),
            roomCode: scenario == "room" ? "ZZZZ" : null,
            playerId: scenario == "player" ? Guid.NewGuid() : null,
            token: scenario == "token" ? "bad" : null,
            questionInstanceId: scenario == "question" ? Guid.NewGuid() : null);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task SameClientId_IsIdempotentBeforeAndAfterReveal()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer, eligibleCount: 1);
        var id = Guid.NewGuid(); var png = await PhotoAnswerTestHarness.DrawingAsync();
        var first = await harness.UploadDrawingAsync(room, room.Players[0], png, clientSubmissionId: id);
        var second = await harness.UploadDrawingAsync(room, room.Players[0], png, clientSubmissionId: id);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode); Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var firstJson = JsonDocument.Parse(await first.Content.ReadAsStringAsync()); using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(firstJson.RootElement.GetProperty("drawingAnswerId").GetGuid(), secondJson.RootElement.GetProperty("drawingAnswerId").GetGuid());
        var counts = await harness.DrawingCountsAsync(room.RoomCode); Assert.Equal((1, 1, 2), (counts.Submissions, counts.Assets, harness.FinalPngCount()));
    }

    [Fact]
    public async Task NewClientIdAfterSubmission_ConflictsWithoutOrphans()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        var png = await PhotoAnswerTestHarness.DrawingAsync();
        Assert.Equal(HttpStatusCode.OK, (await harness.UploadDrawingAsync(room, room.Players[0], png)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await harness.UploadDrawingAsync(room, room.Players[0], png)).StatusCode);
        var counts = await harness.DrawingCountsAsync(room.RoomCode); Assert.Equal((1, 1, 2), (counts.Submissions, counts.Assets, harness.FinalPngCount()));
    }

    private static async Task<byte[]> SinglePixelPng()
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(400, 400, SixLabors.ImageSharp.Color.White);
        image[0, 0] = SixLabors.ImageSharp.Color.Black;
        await using var stream = new MemoryStream(); await image.SaveAsPngAsync(stream); return stream.ToArray();
    }
}
