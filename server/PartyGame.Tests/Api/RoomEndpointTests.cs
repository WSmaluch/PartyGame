using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class RoomEndpointTests(PartyGameApiFactory factory) : IClassFixture<PartyGameApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateAndJoin_ReturnExpectedPublicSnapshots()
    {
        var host = await CreateRoomAsync("  Wojtek  ");
        Assert.Equal(HttpStatusCode.Created, host.Response.StatusCode);
        Assert.Matches("^[A-HJ-NP-Z2-9]{4}$", host.Body.RoomCode);
        Assert.Single(host.Body.Snapshot.Players);
        Assert.Equal("Wojtek", host.Body.Snapshot.Players[0].Nickname);
        Assert.True(host.Body.Snapshot.Players[0].IsHost);
        Assert.False(host.Body.Snapshot.Players[0].IsConnected);
        Assert.Equal(1, host.Body.Snapshot.StateVersion);

        var kasia = await JoinAsync(host.Body.RoomCode, "Kasia");
        var arek = await JoinAsync(host.Body.RoomCode.ToLowerInvariant(), "Arek");
        Assert.Equal(HttpStatusCode.Created, kasia.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, arek.Response.StatusCode);
        Assert.Equal(3, arek.Body.Snapshot.Players.Count);

        var snapshotResponse = await _client.GetAsync($"/api/rooms/{host.Body.RoomCode}");
        var json = await snapshotResponse.Content.ReadAsStringAsync();
        var snapshot = JsonSerializer.Deserialize<RoomSnapshot>(json, JsonOptions)!;
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
        Assert.Equal(3, snapshot.Players.Count);
        Assert.DoesNotContain("reconnectToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tokenHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storageKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Join_RejectsMissingRoomDuplicateNicknameFullRoomAndStartedRoom()
    {
        var missing = await _client.PostAsJsonAsync("/api/rooms/ZZZZ/players", new JoinRoomRequest("Kasia"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        var host = (await CreateRoomAsync("HostA")).Body;
        await JoinAsync(host.RoomCode, "Kasia");
        var duplicate = await _client.PostAsJsonAsync($"/api/rooms/{host.RoomCode}/players", new JoinRoomRequest("  kASIA "));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        for (var index = 2; index < GameRoom.MaximumPlayers; index++)
        {
            var joined = await JoinAsync(host.RoomCode, $"P{index}");
            Assert.Equal(HttpStatusCode.Created, joined.Response.StatusCode);
        }
        var full = await _client.PostAsJsonAsync($"/api/rooms/{host.RoomCode}/players", new JoinRoomRequest("Extra"));
        Assert.Equal(HttpStatusCode.Conflict, full.StatusCode);

        var startedHost = (await CreateRoomAsync("HostB")).Body;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var room = await dbContext.GameRooms.SingleAsync(candidate => candidate.Code == startedHost.RoomCode);
            room.Phase = RoomPhase.Started;
            await dbContext.SaveChangesAsync();
        }
        var afterStart = await _client.PostAsJsonAsync($"/api/rooms/{startedHost.RoomCode}/players", new JoinRoomRequest("Late"));
        Assert.Equal(HttpStatusCode.Conflict, afterStart.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("                     ")]
    public async Task Create_RejectsInvalidNickname(string nickname)
    {
        var response = await _client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest(nickname, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Resume_ValidatesTokenWithoutConnectingPlayer()
    {
        var host = (await CreateRoomAsync("ResumeHost")).Body;
        using var validRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{host.RoomCode}/players/{host.PlayerId}/resume");
        validRequest.Headers.Add("X-Player-Token", host.ReconnectToken);
        var validResponse = await _client.SendAsync(validRequest);
        var resumed = await validResponse.Content.ReadFromJsonAsync<ResumePlayerResponse>(JsonOptions);
        Assert.Equal(HttpStatusCode.OK, validResponse.StatusCode);
        Assert.NotNull(resumed);
        Assert.False(resumed.Player.IsConnected);

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{host.RoomCode}/players/{host.PlayerId}/resume");
        invalidRequest.Headers.Add("X-Player-Token", "wrong-token");
        var invalidResponse = await _client.SendAsync(invalidRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);
    }

    [Fact]
    public async Task ProfilePhoto_AcceptsJpegAndPngReplacesAndReturnsNoStore()
    {
        var host = (await CreateRoomAsync("PhotoHost")).Body;
        var jpeg = await PhotoAnswerTestHarness.ImageAsync();
        var first = await UploadAsync(host, jpeg, "image/jpeg", "untrusted/../../photo.jpg");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        MediaAsset firstAsset;
        await using (var firstScope = factory.Services.CreateAsyncScope())
        {
            var firstDb = firstScope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            firstAsset = await firstDb.MediaAssets.SingleAsync(asset => asset.MediaKind == PartyGame.Domain.Game.MediaKind.ProfilePhoto);
        }

        var png = await PhotoAnswerTestHarness.ImageAsync(png: true);
        var second = await UploadAsync(host, png, "image/png", "photo.png");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var photoResponse = await _client.GetAsync($"/api/rooms/{host.RoomCode}/players/{host.PlayerId}/profile-photo");
        Assert.Equal(HttpStatusCode.OK, photoResponse.StatusCode);
        Assert.Equal("image/jpeg", photoResponse.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", photoResponse.Headers.CacheControl?.ToString());
        Assert.True((await photoResponse.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var player = await db.Players.SingleAsync(player => player.Id == host.PlayerId);
        var asset = await db.MediaAssets.SingleAsync(asset => asset.Id == player.ProfilePhotoMediaAssetId);
        Assert.Equal(PartyGame.Domain.Game.MediaKind.ProfilePhoto, asset.MediaKind);
        Assert.Equal(host.PlayerId, asset.PlayerId);
        Assert.Single(await db.MediaAssets.Where(asset => asset.MediaKind == PartyGame.Domain.Game.MediaKind.ProfilePhoto).ToListAsync());
        Assert.False(File.Exists(MediaStoragePathResolver.ResolveStoragePath(factory.MediaRootPath, firstAsset.DisplayStorageKey)));
        Assert.False(File.Exists(MediaStoragePathResolver.ResolveStoragePath(factory.MediaRootPath, firstAsset.ThumbnailStorageKey)));
    }

    [Fact]
    public async Task ProfilePhoto_RejectsOversizedUnsupportedAndMismatchedFiles()
    {
        var host = (await CreateRoomAsync("BadPhoto")).Body;
        var oversized = new byte[5 * 1024 * 1024 + 1];
        oversized[0] = 0xff; oversized[1] = 0xd8; oversized[2] = 0xff;
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(host, oversized, "image/jpeg", "large.jpg")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(host, [1, 2, 3], "text/plain", "text.txt")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(host, [1, 2, 3], "image/jpeg", "fake.jpg")).StatusCode);
    }

    private async Task<(HttpResponseMessage Response, RoomAccessResponse Body)> CreateRoomAsync(string nickname)
    {
        var response = await _client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest(nickname, null, null, null));
        return (response, (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!);
    }

    private async Task<(HttpResponseMessage Response, RoomAccessResponse Body)> JoinAsync(string code, string nickname)
    {
        var response = await _client.PostAsJsonAsync($"/api/rooms/{code}/players", new JoinRoomRequest(nickname));
        return (response, (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!);
    }

    private async Task<HttpResponseMessage> UploadAsync(RoomAccessResponse access, byte[] bytes, string contentType, string fileName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{access.RoomCode}/players/{access.PlayerId}/profile-photo");
        request.Headers.Add("X-Player-Token", access.ReconnectToken);
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        request.Content = new MultipartFormDataContent { { file, "file", fileName } };
        return await _client.SendAsync(request);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
