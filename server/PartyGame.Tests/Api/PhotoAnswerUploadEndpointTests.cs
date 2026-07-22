using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class PhotoAnswerUploadEndpointTests
{
    [Theory]
    [InlineData(false, "image/jpeg")]
    [InlineData(true, "image/png")]
    public async Task ValidJpegAndPng_AreAcceptedAndPersistedAsJpeg(bool png, string contentType)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        var response = await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync(png: png), contentType);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((1, 1, 0, 0), (await harness.CountsAsync(room.RoomCode)) switch { var c => (c.Submissions, c.Assets, c.Votes, c.Scores) });
        Assert.Equal(2, harness.FinalJpegCount());
        foreach (var path in Directory.EnumerateFiles(harness.Factory.MediaRootPath, "*.jpg", SearchOption.AllDirectories))
        {
            var signature = await File.ReadAllBytesAsync(path);
            Assert.True(signature.Length > 2 && signature[0] == 0xff && signature[1] == 0xd8);
        }
    }

    [Fact]
    public async Task MissingPhoto_ReturnsControlledBadRequest()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync();
        var response = await harness.UploadAsync(room, room.Players[0], null);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "photo_answer_file_missing");
    }

    [Theory]
    [InlineData("empty", "image/jpeg", "photo_answer_file_empty")]
    [InlineData("bad-jpeg", "image/jpeg", "photo_answer_invalid_image")]
    [InlineData("bad-png", "image/png", "photo_answer_invalid_image")]
    [InlineData("unsupported", "text/plain", "photo_answer_invalid_content_type")]
    public async Task InvalidPayloads_ReturnControlledBadRequest(string kind, string contentType, string expectedCode)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync();
        var bytes = kind == "empty" ? [] : "not an image"u8.ToArray();
        var response = await harness.UploadAsync(room, room.Players[0], bytes, contentType);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, expectedCode);
        Assert.Equal((0, 0), (await harness.CountsAsync(room.RoomCode)) switch { var c => (c.Submissions, c.Assets) });
    }

    [Theory]
    [InlineData(true, "image/jpeg")]
    [InlineData(false, "image/png")]
    public async Task DeclaredMimeMustMatchDecodedImage(bool actualPng, string declaredMime)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync();
        var response = await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync(png: actualPng), declaredMime);
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "photo_answer_invalid_image");
    }

    [Theory]
    [InlineData("MediaStorage:MaximumUploadBytes", "100", 640, 480, "photo_answer_file_too_large")]
    [InlineData("MediaStorage:MaximumImageWidth", "500", 640, 480, "photo_answer_dimensions_too_large")]
    [InlineData("MediaStorage:MaximumImageHeight", "400", 640, 480, "photo_answer_dimensions_too_large")]
    [InlineData("MediaStorage:MinimumImageWidth", "700", 640, 480, "photo_answer_dimensions_too_small")]
    public async Task ConfiguredSizeLimits_AreEnforced(string setting, string value, int width, int height, string expectedCode)
    {
        await using var harness = new PhotoAnswerTestHarness(settings: new Dictionary<string, string?> { [setting] = value });
        var room = await harness.CreateRoomAsync();
        var response = await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync(width, height));
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, expectedCode);
    }

    [Theory]
    [InlineData("missing-room", HttpStatusCode.NotFound)]
    [InlineData("missing-player", HttpStatusCode.NotFound)]
    [InlineData("bad-token", HttpStatusCode.Unauthorized)]
    [InlineData("old-question", HttpStatusCode.Conflict)]
    public async Task IdentityAndRouteFailures_ReturnExpectedStatus(string scenario, HttpStatusCode status)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync();
        var response = await harness.UploadAsync(
            room,
            room.Players[0],
            await PhotoAnswerTestHarness.ImageAsync(),
            roomCode: scenario == "missing-room" ? "ZZZZ" : null,
            playerId: scenario == "missing-player" ? Guid.NewGuid() : null,
            token: scenario == "bad-token" ? "invalid" : null,
            questionInstanceId: scenario == "old-question" ? Guid.NewGuid() : null);
        Assert.Equal(status, response.StatusCode);
    }

    [Theory]
    [InlineData(QuestionType.PlayerSelection)]
    [InlineData(QuestionType.TextAnswer)]
    public async Task NonPhotoQuestions_RejectUpload(QuestionType questionType)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(questionType: questionType);
        await AssertProblemAsync(
            await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync()),
            HttpStatusCode.Conflict,
            "photo_answer_not_active");
    }

    [Theory]
    [InlineData(GameStage.QuestionIntro, "photo_answer_not_active")]
    [InlineData(GameStage.RevealingPhotoAnswers, "photo_answer_not_active")]
    [InlineData(GameStage.PausedForDisplay, "photo_answer_not_active")]
    public async Task WrongOrPausedStage_RejectsUpload(GameStage stage, string expectedCode)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(stage: stage);
        var before = await harness.CountsAsync(room.RoomCode);
        await AssertProblemAsync(
            await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync()),
            HttpStatusCode.Conflict,
            expectedCode);
        var after = await harness.CountsAsync(room.RoomCode);
        Assert.Equal((before.Submissions, before.Assets, before.Version), (after.Submissions, after.Assets, after.Version));
        Assert.Equal(0, harness.FinalJpegCount());
    }

    [Fact]
    public async Task ExpiredStage_RejectsUpload()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(stageEndsAtUtc: DateTimeOffset.UtcNow.AddMinutes(-1));
        await AssertProblemAsync(
            await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync()),
            HttpStatusCode.Conflict,
            "photo_answer_time_expired");
    }

    [Fact]
    public async Task IneligiblePlayer_RejectsUpload()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        await AssertProblemAsync(
            await harness.UploadAsync(room, room.Players[2], await PhotoAnswerTestHarness.ImageAsync()),
            HttpStatusCode.Conflict,
            "photo_answer_player_not_eligible");
    }

    [Fact]
    public async Task NewClientSubmissionId_ConflictsWithoutCreatingOrphanFiles()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        var image = await PhotoAnswerTestHarness.ImageAsync();
        Assert.Equal(HttpStatusCode.OK, (await harness.UploadAsync(room, room.Players[0], image)).StatusCode);
        await AssertProblemAsync(
            await harness.UploadAsync(room, room.Players[0], image),
            HttpStatusCode.Conflict,
            "photo_answer_already_submitted");
        Assert.Equal((1, 1, 2), (await harness.CountsAsync(room.RoomCode)) switch { var c => (c.Submissions, c.Assets, harness.FinalJpegCount()) });
    }

    [Fact]
    public async Task SameClientSubmissionId_IsIdempotentBeforeAndAfterReveal()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        var image = await PhotoAnswerTestHarness.ImageAsync();
        var clientId = Guid.NewGuid();
        var first = await harness.UploadAsync(room, room.Players[0], image, clientSubmissionId: clientId);
        var firstJson = await first.Content.ReadAsStringAsync();
        var afterFirst = await harness.CountsAsync(room.RoomCode);
        var retry = await harness.UploadAsync(room, room.Players[0], image, clientSubmissionId: clientId);
        var retryJson = await retry.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(PhotoId(firstJson), PhotoId(retryJson));
        Assert.Equal(afterFirst, await harness.CountsAsync(room.RoomCode));
        Assert.Equal(2, harness.FinalJpegCount());

        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.SingleAsync(candidate => candidate.Id == room.GameSessionId);
            session.Stage = GameStage.RevealingPhotoAnswers;
            await db.SaveChangesAsync();
        }
        var afterReveal = await harness.UploadAsync(room, room.Players[0], image, clientSubmissionId: clientId);
        Assert.Equal(HttpStatusCode.OK, afterReveal.StatusCode);
        Assert.Equal(PhotoId(firstJson), PhotoId(await afterReveal.Content.ReadAsStringAsync()));
        Assert.Equal(afterFirst, await harness.CountsAsync(room.RoomCode));
    }

    [Fact]
    public async Task ResponseAndPublicSnapshot_DoNotLeakStorageInternals()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        var response = await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync());
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mediaAssetId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(harness.Factory.MediaRootPath, json, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Equal(code, JsonDocument.Parse(json).RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("/Users/", json, StringComparison.Ordinal);
    }

    private static Guid PhotoId(string json) => JsonDocument.Parse(json).RootElement.GetProperty("photoAnswerId").GetGuid();
}
