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
