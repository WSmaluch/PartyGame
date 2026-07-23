using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Rooms;

namespace PartyGame.Tests.Api;

public sealed class LobbySignalRTests(PartyGameApiFactory factory) : IClassFixture<PartyGameApiFactory>
{
    private readonly HttpClient _httpClient = factory.CreateClient();

    [Fact]
    public async Task LobbyFlow_StartsOnceAndSupportsDisconnectAndReconnect()
    {
        var host = await CreateRoomAsync("Wojtek");
        var kasia = await JoinAsync(host.RoomCode, "Kasia");
        var arek = await JoinAsync(host.RoomCode, "Arek");
        await using var display = CreateConnection();
        await using var hostConnection = CreateConnection();
        await using var kasiaConnection = CreateConnection();
        await using var arekConnection = CreateConnection();
        var snapshotReceived = new TaskCompletionSource<RoomSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<RoomSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startedCount = 0;
        display.On<RoomSnapshot>("RoomSnapshotUpdated", snapshot => snapshotReceived.TrySetResult(snapshot));
        display.On<RoomSnapshot>("RoomStarted", snapshot =>
        {
            Interlocked.Increment(ref startedCount);
            started.TrySetResult(snapshot);
        });

        await Task.WhenAll(display.StartAsync(), hostConnection.StartAsync(), kasiaConnection.StartAsync(), arekConnection.StartAsync());
        var displaySnapshot = await display.InvokeAsync<RoomSnapshot>("AttachDisplay", host.RoomCode);
        Assert.True(displaySnapshot.DisplayConnected);
        Assert.True((await snapshotReceived.Task.WaitAsync(TimeSpan.FromSeconds(5))).DisplayConnected);

        var attached = await Task.WhenAll(
            AttachPlayerAsync(hostConnection, host),
            AttachPlayerAsync(kasiaConnection, kasia),
            AttachPlayerAsync(arekConnection, arek));
        Assert.All(attached, snapshot => Assert.Contains(snapshot.Players, player => player.IsConnected));

        await Task.WhenAll(
            UploadJpegAsync(host),
            UploadJpegAsync(kasia),
            UploadJpegAsync(arek));

        await Task.WhenAll(
            SetReadyAsync(hostConnection, host),
            SetReadyAsync(kasiaConnection, kasia),
            SetReadyAsync(arekConnection, arek));
        var startedSnapshot = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RoomPhase.Started, startedSnapshot.Phase);
        Assert.All(startedSnapshot.Players, player =>
        {
            Assert.True(player.IsConnected);
            Assert.True(player.HasProfilePhoto);
            Assert.True(player.IsReady);
        });
        await Task.Delay(150);
        Assert.Equal(1, Volatile.Read(ref startedCount));

        var lateJoin = await _httpClient.PostAsJsonAsync($"/api/rooms/{host.RoomCode}/players", new JoinRoomRequest("Late"));
        Assert.Equal(HttpStatusCode.Conflict, lateJoin.StatusCode);

        await kasiaConnection.StopAsync();
        await WaitForAsync(async () => !(await GetSnapshotAsync(host.RoomCode)).Players.Single(player => player.Id == kasia.PlayerId).IsConnected);
        await using var reconnectedKasia = CreateConnection();
        await reconnectedKasia.StartAsync();
        var resumed = await AttachPlayerAsync(reconnectedKasia, kasia);
        Assert.True(resumed.Players.Single(player => player.Id == kasia.PlayerId).IsConnected);
        Assert.Equal(RoomPhase.Started, resumed.Phase);
        Assert.Equal(1, Volatile.Read(ref startedCount));
    }

    [Fact]
    public async Task AttachPlayer_RejectsInvalidToken()
    {
        var host = await CreateRoomAsync("TokenHost");
        await using var connection = CreateConnection();
        await connection.StartAsync();
        var exception = await Assert.ThrowsAsync<HubException>(() =>
            connection.InvokeAsync<RoomSnapshot>("AttachPlayer", host.RoomCode, host.PlayerId, "wrong-token"));
        Assert.Contains("invalid or expired", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AttachDisplay_ReplacesOldDisplayAndIgnoresItsDisconnect()
    {
        var host = await CreateRoomAsync("DisplayHost");
        await using var first = CreateConnection();
        await using var second = CreateConnection();
        var replaced = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.On("DisplayReplaced", () => replaced.TrySetResult());
        await Task.WhenAll(first.StartAsync(), second.StartAsync());
        await first.InvokeAsync<RoomSnapshot>("AttachDisplay", host.RoomCode);
        await second.InvokeAsync<RoomSnapshot>("AttachDisplay", host.RoomCode);
        await replaced.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await first.StopAsync();
        Assert.True((await second.InvokeAsync<RoomSnapshot>("GetRoomSnapshot", host.RoomCode)).DisplayConnected);
        await second.StopAsync();
        await WaitForAsync(async () => !(await GetSnapshotAsync(host.RoomCode)).DisplayConnected);
    }

    [Fact]
    public async Task NewPlayerConnection_ReplacesOldWithoutStaleDisconnectMarkingOffline()
    {
        var host = await CreateRoomAsync("ReconnectHost");
        await using var first = CreateConnection();
        await using var second = CreateConnection();
        await Task.WhenAll(first.StartAsync(), second.StartAsync());
        await AttachPlayerAsync(first, host);
        await AttachPlayerAsync(second, host);
        await Assert.ThrowsAsync<HubException>(() =>
            first.InvokeAsync<RoomSnapshot>("SetReady", host.RoomCode, host.PlayerId, host.ReconnectToken, false));
        await first.StopAsync();
        Assert.True((await second.InvokeAsync<RoomSnapshot>("GetRoomSnapshot", host.RoomCode)).Players.Single().IsConnected);
    }

    private HubConnection CreateConnection() => new HubConnectionBuilder()
        .WithUrl("http://localhost/hubs/game", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
        })
        .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
        .Build();

    private static Task<RoomSnapshot> AttachPlayerAsync(HubConnection connection, RoomAccessResponse access) =>
        connection.InvokeAsync<RoomSnapshot>("AttachPlayer", access.RoomCode, access.PlayerId, access.ReconnectToken);

    private static Task<RoomSnapshot> SetReadyAsync(HubConnection connection, RoomAccessResponse access) =>
        connection.InvokeAsync<RoomSnapshot>("SetReady", access.RoomCode, access.PlayerId, access.ReconnectToken, true);

    private async Task<RoomAccessResponse> CreateRoomAsync(string nickname)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/rooms", new CreateRoomRequest(nickname, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!;
    }

    private async Task<RoomAccessResponse> JoinAsync(string roomCode, string nickname)
    {
        var response = await _httpClient.PostAsJsonAsync($"/api/rooms/{roomCode}/players", new JoinRoomRequest(nickname));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!;
    }

    private async Task UploadJpegAsync(RoomAccessResponse access)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{access.RoomCode}/players/{access.PlayerId}/profile-photo");
        request.Headers.Add("X-Player-Token", access.ReconnectToken);
        var content = new ByteArrayContent(await PhotoAnswerTestHarness.ImageAsync());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        request.Content = new MultipartFormDataContent { { content, "file", "profile.jpg" } };
        (await _httpClient.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private async Task<RoomSnapshot> GetSnapshotAsync(string roomCode) =>
        (await _httpClient.GetFromJsonAsync<RoomSnapshot>($"/api/rooms/{roomCode}", JsonOptions))!;

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (!await condition())
        {
            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException("The expected lobby state was not observed.");
            }
            await Task.Delay(25);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
