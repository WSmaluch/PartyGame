using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Api.Hubs;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

/// <summary>
/// Contract guard for the exact failure that was visible on physical devices:
/// all already-attached players must receive their own actionable Final Selfie
/// state when the real state machine enters the final round.
/// </summary>
public sealed class FinalRoundPrivateStateContractTests
{
    [Fact]
    public async Task ThreeAttachedPlayers_ReceiveOwnActionableFinalSelfieContractOverSignalRAndResume()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync(GameStage.RoundSummary, QuestionType.PlayerSelection, playerCount: 3, eligibleCount: 0, stageEndsAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        await EnableFinalRoundAsync(harness, room);

        var received = new ConcurrentDictionary<Guid, PlayerPrivateGameState>();
        await using var display = Connection(harness);
        var clients = room.Players.Select(player => Connection(harness)).ToArray();
        foreach (var (player, client) in room.Players.Zip(clients))
        {
            client.On<PlayerPrivateGameState>("PlayerPrivateGameStateUpdated", state =>
            {
                if (state.PlayerId == player.PlayerId && state.FinalRound?.CanSubmitSelfie == true)
                    received.TryAdd(player.PlayerId, state);
            });
        }
        var displayReceivedPrivateState = false;
        display.On<PlayerPrivateGameState>("PlayerPrivateGameStateUpdated", _ => displayReceivedPrivateState = true);

        try
        {
            await Task.WhenAll(clients.Append(display).Select(client => client.StartAsync()));
            await display.InvokeAsync("AttachDisplay", room.RoomCode);
            // Attach mutates connection/presence state in SQLite.  Keep fixture
            // setup deterministic; the assertion below still verifies delivery
            // to all three already-active SignalR connections.
            foreach (var pair in room.Players.Zip(clients))
                await pair.Second.InvokeAsync("AttachPlayer", room.RoomCode, pair.First.PlayerId, pair.First.Token);

            // This goes through the actual RoundSummary -> CollectingFinalSelfies
            // transition. The notifier is the same singleton used by the timeout worker.
            Assert.Equal(GameStage.CollectingFinalSelfies, await harness.ProcessAtAsync(room, DateTimeOffset.UtcNow));
            await using var notificationScope = harness.Factory.Services.CreateAsyncScope();
            var notifier = notificationScope.ServiceProvider.GetRequiredService<RoomNotifier>();
            var roomService = notificationScope.ServiceProvider.GetRequiredService<IRoomService>();
            await notifier.NotifyAsync(new RoomMutationResult(await roomService.GetAsync(room.RoomCode), true, false));

            await WaitUntilAsync(() => received.Count == room.Players.Count, TimeSpan.FromSeconds(5), "SignalR private Final Selfie contracts");
            Assert.False(displayReceivedPrivateState);

            var expected = await ExpectedArtifactsAsync(harness, room);
            foreach (var player in room.Players)
            {
                var state = received[player.PlayerId];
                var artifact = expected[player.PlayerId];
                Assert.Equal(room.GameSessionId, state.QuestionInstanceId);
                Assert.NotNull(state.FinalRound);
                Assert.False(state.FinalRound!.HasSubmittedSelfie);
                Assert.True(state.FinalRound.CanSubmitSelfie);
                Assert.Equal(artifact.SelfiePromptPl, state.FinalRound.SelfiePrompt?.Pl);
                Assert.Equal(artifact.SelfiePromptEn, state.FinalRound.SelfiePrompt?.En);
                Assert.Equal(artifact.TargetRolePl, state.FinalRound.TargetRole?.Pl);

                using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{room.RoomCode}/players/{player.PlayerId}/resume");
                request.Headers.Add("X-Player-Token", player.Token);
                using var response = await harness.Client.SendAsync(request);
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync();
                var contractJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                contractJson.Converters.Add(new JsonStringEnumConverter());
                var payload = JsonSerializer.Deserialize<ResumePlayerResponse>(body, contractJson);
                Assert.NotNull(payload?.PrivateState.FinalRound);
                Assert.True(payload!.PrivateState.FinalRound!.CanSubmitSelfie);

                // Assert the wire shape as a client receives it, not only C# state.
                using var json = JsonDocument.Parse(body);
                var final = json.RootElement.GetProperty("privateState").GetProperty("finalRound");
                Assert.True(final.GetProperty("canSubmitSelfie").GetBoolean());
                Assert.Equal(artifact.SelfiePromptPl, final.GetProperty("selfiePrompt").GetProperty("pl").GetString());
                Assert.Equal(artifact.SelfiePromptEn, final.GetProperty("selfiePrompt").GetProperty("en").GetString());
            }
        }
        finally
        {
            await Task.WhenAll(clients.Select(client => client.DisposeAsync().AsTask()));
        }
    }

    private static HubConnection Connection(PhotoAnswerTestHarness harness) => new HubConnectionBuilder()
        .WithUrl("http://localhost/hubs/game", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => harness.Factory.Server.CreateHandler();
        })
        .Build();

    private static async Task EnableFinalRoundAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var session = await db.GameSessions.Include(session => session.Room).ThenInclude(room => room.Settings).SingleAsync(session => session.Id == room.GameSessionId);
        session.Room.Settings.FinalRoundEnabled = true;
        session.TotalRounds = 2;
        await db.SaveChangesAsync();
    }

    private static async Task<IReadOnlyDictionary<Guid, (string SelfiePromptPl, string SelfiePromptEn, string TargetRolePl)>> ExpectedArtifactsAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var state = FinalRoundState.Read(await db.GameSessions.Where(session => session.Id == room.GameSessionId).Select(session => session.FinalRoundStateJson).SingleAsync())!;
        return state.Artifacts.ToDictionary(artifact => artifact.SubjectPlayerId, artifact => (artifact.SelfiePromptPl, artifact.SelfiePromptEn, artifact.TargetRolePl));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string description)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(25);
        }
        throw new TimeoutException($"Timed out waiting for {description}.");
    }
}
