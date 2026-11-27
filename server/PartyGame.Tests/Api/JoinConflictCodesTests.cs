using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PartyGame.Api.Contracts;

namespace PartyGame.Tests.Api;

public sealed class JoinConflictCodesTests : IDisposable
{
    private readonly PartyGameApiFactory factory = new(
        Path.Combine(Path.GetTempPath(), "PartyGame.JoinConflictCodes.Tests", Guid.NewGuid().ToString("N")),
        settings: new Dictionary<string, string?> { ["GameFlow:WorkerIntervalMilliseconds"] = "60000" });

    public void Dispose() => factory.Dispose();

    [Fact]
    public async Task Join_WithTakenNickname_ReturnsNicknameTakenCode()
    {
        using var client = factory.CreateClient();
        var roomCode = await CreateRoomAsync(client, "Host");

        var response = await client.PostAsJsonAsync($"/api/rooms/{roomCode}/players", new JoinRoomRequest(" host "));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("NICKNAME_TAKEN", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task Join_WhenRoomIsFull_ReturnsRoomFullCode()
    {
        using var client = factory.CreateClient();
        var roomCode = await CreateRoomAsync(client, "Host");
        for (var index = 1; index < 10; index++)
        {
            var joined = await client.PostAsJsonAsync($"/api/rooms/{roomCode}/players", new JoinRoomRequest($"Player{index}"));
            Assert.Equal(HttpStatusCode.Created, joined.StatusCode);
        }

        var response = await client.PostAsJsonAsync($"/api/rooms/{roomCode}/players", new JoinRoomRequest("Eleventh"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ROOM_FULL", await ErrorCodeAsync(response));
    }

    private static async Task<string> CreateRoomAsync(HttpClient client, string nickname)
    {
        var response = await client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest(nickname, null, null, null));
        response.EnsureSuccessStatusCode();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("roomCode").GetString()!;
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
