using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class ProfilePhotoRestartPersistenceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task RealHostRestart_PreservesProfileMediaAndMetadata()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.ProfileRestart", Guid.NewGuid().ToString("N"));
        RoomAccessResponse access;
        Guid mediaAssetId;
        try
        {
            await using (var first = new PartyGameApiFactory(directory, deleteOnDispose: false))
            {
                var client = first.CreateClient();
                var create = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest("Host", null, null, null));
                create.EnsureSuccessStatusCode();
                access = (await create.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!;
                await UploadProfileAsync(client, access);

                await using var scope = first.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
                var player = await db.Players.SingleAsync(player => player.Id == access.PlayerId);
                mediaAssetId = player.ProfilePhotoMediaAssetId!.Value;
                var asset = await db.MediaAssets.SingleAsync(asset => asset.Id == mediaAssetId);
                Assert.Equal(MediaKind.ProfilePhoto, asset.MediaKind);
                Assert.Equal(access.PlayerId, asset.PlayerId);
                Assert.Equal(player.RoomId, asset.RoomId);
                Assert.Null(asset.QuestionInstanceId);
            }

            await using (var second = new PartyGameApiFactory(directory, deleteOnDispose: false))
            {
                var client = second.CreateClient();
                var response = await client.GetAsync($"/api/rooms/{access.RoomCode}/players/{access.PlayerId}/profile-photo");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("image/jpeg", response.Content.Headers.ContentType?.MediaType);
                Assert.True((await response.Content.ReadAsByteArrayAsync()).AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff }));

                await using var scope = second.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
                Assert.Equal(mediaAssetId, await db.Players.Where(player => player.Id == access.PlayerId).Select(player => player.ProfilePhotoMediaAssetId).SingleAsync());
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RealHostRestart_LeaveLobbyRetiresPlayerWithReferencedProfileMedia()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.LobbyLeaveRestart", Guid.NewGuid().ToString("N"));
        RoomAccessResponse host;
        RoomAccessResponse leaving;
        Guid mediaAssetId;
        try
        {
            await using (var first = new PartyGameApiFactory(directory, deleteOnDispose: false))
            {
                var client = first.CreateClient();
                host = await CreateRoomAsync(client, "Host");
                leaving = await JoinAsync(client, host.RoomCode, "Leaving");
                _ = await JoinAsync(client, host.RoomCode, "Third");
                await UploadProfileAsync(client, leaving);

                await using var scope = first.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
                mediaAssetId = (await db.Players.SingleAsync(player => player.Id == leaving.PlayerId)).ProfilePhotoMediaAssetId!.Value;
                Assert.Equal(leaving.PlayerId, (await db.MediaAssets.SingleAsync(asset => asset.Id == mediaAssetId)).PlayerId);
            }

            await using (var second = new PartyGameApiFactory(directory, deleteOnDispose: false))
            {
                await using var scope = second.Services.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IRoomService>();
                var attached = await service.AttachPlayerAsync(leaving.RoomCode, leaving.PlayerId, leaving.ReconnectToken);
                Assert.Contains(attached.Room.Players, player => player.Id == leaving.PlayerId && player.IsConnected);

                var left = await service.LeaveLobbyAsync(leaving.RoomCode, leaving.PlayerId, leaving.ReconnectToken);
                Assert.Equal(2, left.Room.Players.Count(player => player.IsActive));
                Assert.DoesNotContain(left.Room.Players, player => player.Id == leaving.PlayerId && player.IsActive);

                var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
                var persisted = await db.Players.SingleAsync(player => player.Id == leaving.PlayerId);
                Assert.False(persisted.IsActive);
                Assert.False(persisted.IsConnected);
                Assert.Null(await db.PlayerSessions.SingleOrDefaultAsync(session => session.PlayerId == leaving.PlayerId));
                Assert.Equal(leaving.PlayerId, (await db.MediaAssets.SingleAsync(asset => asset.Id == mediaAssetId)).PlayerId);

                await Assert.ThrowsAsync<PlayerNotFoundException>(() => service.ResumeAsync(leaving.RoomCode, leaving.PlayerId, leaving.ReconnectToken));
                var rejoined = await service.JoinAsync(leaving.RoomCode, "Leaving");
                Assert.NotEqual(leaving.PlayerId, rejoined.Player.Id);
                Assert.True(rejoined.Player.IsActive);
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<RoomAccessResponse> CreateRoomAsync(HttpClient client, string nickname)
    {
        var response = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest(nickname, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!;
    }

    private static async Task<RoomAccessResponse> JoinAsync(HttpClient client, string roomCode, string nickname)
    {
        var response = await client.PostAsJsonAsync($"/api/rooms/{roomCode}/players", new JoinRoomRequest(nickname));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!;
    }

    private static async Task UploadProfileAsync(HttpClient client, RoomAccessResponse access)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{access.RoomCode}/players/{access.PlayerId}/profile-photo");
        request.Headers.Add("X-Player-Token", access.ReconnectToken);
        var file = new ByteArrayContent(await PhotoAnswerTestHarness.ImageAsync());
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        request.Content = new MultipartFormDataContent { { file, "file", "profile.jpg" } };
        (await client.SendAsync(request)).EnsureSuccessStatusCode();
    }
}
