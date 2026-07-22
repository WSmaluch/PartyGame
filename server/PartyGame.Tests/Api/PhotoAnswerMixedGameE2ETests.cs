using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class PhotoAnswerMixedGameE2ETests
{
    [Fact]
    public async Task RealHostAndSignalRClient_RunExactTwoTwoTwoPlanToCompleted()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var settings = new RoomSettingsRequest(1, 6, 5, 5, 5, 10, 30, 3, false, 1);
        var host = await CreateAsync(harness, "Host", settings);
        var second = await JoinAsync(harness, host.RoomCode, "Second");
        var third = await JoinAsync(harness, host.RoomCode, "Third");
        var players = new[] { host, second, third };
        await using var display = Connection(harness);
        await using var firstConnection = Connection(harness);
        await using var secondConnection = Connection(harness);
        await using var thirdConnection = Connection(harness);
        var connections = new[] { firstConnection, secondConnection, thirdConnection };
        await Task.WhenAll(connections.Append(display).Select(connection => connection.StartAsync()));
        await display.InvokeAsync<RoomSnapshot>("AttachDisplay", host.RoomCode);
        await Task.WhenAll(players.Zip(connections).Select(pair => pair.Second.InvokeAsync<RoomSnapshot>("AttachPlayer", pair.First.RoomCode, pair.First.PlayerId, pair.First.ReconnectToken)));
        await Task.WhenAll(players.Select(player => UploadProfileAsync(harness, player)));
        await Task.WhenAll(players.Zip(connections).Select(pair => pair.Second.InvokeAsync<RoomSnapshot>("SetReady", pair.First.RoomCode, pair.First.PlayerId, pair.First.ReconnectToken, true)));

        var seenInstances = new HashSet<Guid>();
        var seenTypes = new Dictionary<QuestionType, int>();
        var seenStages = new HashSet<GameStage>();
        var handledInteractiveStages = new HashSet<(Guid InstanceId, GameStage Stage)>();
        for (var iteration = 0; iteration < 60; iteration++)
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.SingleAsync();
            seenStages.Add(session.Stage);
            if (session.Stage == GameStage.Completed) break;
            if (session.CurrentQuestionInstanceId is Guid instanceId)
            {
                var question = await db.GameQuestionInstances.Where(instance => instance.Id == instanceId).Select(instance => new { instance.Id, instance.Question.Type }).SingleAsync();
                if (seenInstances.Add(question.Id)) seenTypes[question.Type] = seenTypes.GetValueOrDefault(question.Type) + 1;
                if (handledInteractiveStages.Add((instanceId, session.Stage)))
                {
                    if (session.Stage == GameStage.CollectingPlayerSelections)
                    {
                        for (var index = 0; index < players.Length; index++)
                            await connections[index].InvokeAsync("SubmitPlayerSelection", players[index].RoomCode, players[index].PlayerId, players[index].ReconnectToken, players[(index + 1) % players.Length].PlayerId);
                        continue;
                    }
                    if (session.Stage == GameStage.CollectingTextAnswers)
                    {
                        var eligibleIds = await db.TextAnswerEligiblePlayers.Where(candidate => candidate.QuestionInstanceId == instanceId).Select(candidate => candidate.PlayerId).ToListAsync();
                        for (var index = 0; index < players.Length; index++)
                            if (eligibleIds.Contains(players[index].PlayerId))
                                await connections[index].InvokeAsync("SubmitTextAnswer", players[index].RoomCode, players[index].PlayerId, players[index].ReconnectToken, $"Anonimowa odpowiedź {index + 1}");
                        continue;
                    }
                    if (session.Stage == GameStage.CollectingTextAnswerVotes)
                    {
                        var answers = await db.TextAnswerSubmissions.Where(candidate => candidate.QuestionInstanceId == instanceId).Select(candidate => new { candidate.Id, candidate.AuthorPlayerId }).ToListAsync();
                        for (var index = 0; index < players.Length; index++)
                        {
                            var selected = answers.First(answer => answer.AuthorPlayerId != players[index].PlayerId).Id;
                            await connections[index].InvokeAsync("SubmitTextAnswerVote", players[index].RoomCode, players[index].PlayerId, players[index].ReconnectToken, selected);
                        }
                        continue;
                    }
                    if (session.Stage == GameStage.CollectingPhotoAnswers)
                    {
                        var jpeg = await PhotoAnswerTestHarness.ImageAsync();
                        await Task.WhenAll(players.Select(player => UploadPhotoAnswerAsync(harness, player, instanceId, jpeg)));
                        continue;
                    }
                    if (session.Stage == GameStage.CollectingPhotoAnswerVotes)
                    {
                        var answers = await db.PhotoAnswerSubmissions.Where(candidate => candidate.QuestionInstanceId == instanceId).Select(candidate => new { candidate.Id, candidate.AuthorPlayerId }).ToListAsync();
                        for (var index = 0; index < players.Length; index++)
                        {
                            var selected = index == 0
                                ? answers.First(answer => answer.AuthorPlayerId == players[index].PlayerId).Id
                                : answers[(index + 1) % answers.Count].Id;
                            await connections[index].InvokeAsync("SubmitPhotoAnswerVote", players[index].RoomCode, players[index].PlayerId, players[index].ReconnectToken, instanceId, selected);
                        }
                        continue;
                    }
                }
            }
            session.StageEndsAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-1);
            await db.SaveChangesAsync();
            var machine = scope.ServiceProvider.GetRequiredService<GameStateMachine>();
            if (await machine.ProcessTransitionAsync(session.Id, DateTimeOffset.UtcNow, CancellationToken.None))
            {
                var room = await db.GameRooms.SingleAsync(candidate => candidate.Code == host.RoomCode);
                room.PublicStateChanged(DateTimeOffset.UtcNow);
                await db.SaveChangesAsync();
            }
        }

        await using (var scope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            Assert.Equal(GameStage.Completed, await db.GameSessions.Select(session => session.Stage).SingleAsync());
            Assert.Equal(6, seenInstances.Count);
            Assert.Equal(2, seenTypes[QuestionType.PlayerSelection]);
            Assert.Equal(2, seenTypes[QuestionType.TextAnswer]);
            Assert.Equal(2, seenTypes[QuestionType.PhotoAnswer]);
            var reasons = await db.ScoreTransactions.Select(transaction => transaction.Reason).Distinct().ToListAsync();
            Assert.Contains("Player Selection Score", reasons);
            Assert.Contains("Text Answer Score", reasons);
            Assert.Contains("PhotoAnswerConformity", reasons);
            Assert.True(await db.Players.SumAsync(player => player.Score) > 0);
        }
        Assert.Contains(GameStage.CollectingPlayerSelections, seenStages);
        Assert.Contains(GameStage.CollectingTextAnswers, seenStages);
        Assert.Contains(GameStage.CollectingPhotoAnswers, seenStages);
        Assert.Contains(GameStage.RoundSummary, seenStages);
        var publicJson = await harness.Client.GetStringAsync($"/api/rooms/{host.RoomCode}");
        Assert.DoesNotContain("storageKey", publicJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reconnectToken", publicJson, StringComparison.OrdinalIgnoreCase);
    }

    private static HubConnection Connection(PhotoAnswerTestHarness harness) => new HubConnectionBuilder()
        .WithUrl("http://localhost/hubs/game", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => harness.Factory.Server.CreateHandler();
        })
        .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
        .Build();

    private static async Task<RoomAccessResponse> CreateAsync(PhotoAnswerTestHarness harness, string nickname, RoomSettingsRequest settings)
    {
        var response = await harness.Client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest(nickname, settings, ["starter"], ["PlayerSelection", "TextAnswer", "PhotoAnswer"]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!;
    }

    private static async Task<RoomAccessResponse> JoinAsync(PhotoAnswerTestHarness harness, string roomCode, string nickname)
    {
        var response = await harness.Client.PostAsJsonAsync($"/api/rooms/{roomCode}/players", new JoinRoomRequest(nickname));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!;
    }

    private static async Task UploadProfileAsync(PhotoAnswerTestHarness harness, RoomAccessResponse player)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{player.RoomCode}/players/{player.PlayerId}/profile-photo");
        request.Headers.Add("X-Player-Token", player.ReconnectToken);
        var file = new ByteArrayContent([0xff, 0xd8, 0xff, 0xe0, 1, 2, 3]);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        request.Content = new MultipartFormDataContent { { file, "file", "profile.jpg" } };
        (await harness.Client.SendAsync(request)).EnsureSuccessStatusCode();
    }

    private static async Task UploadPhotoAnswerAsync(PhotoAnswerTestHarness harness, RoomAccessResponse player, Guid questionInstanceId, byte[] jpeg)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(player.PlayerId.ToString()), "playerId");
        form.Add(new StringContent(player.ReconnectToken), "reconnectToken");
        form.Add(new StringContent(Guid.NewGuid().ToString()), "clientSubmissionId");
        var file = new ByteArrayContent(jpeg);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
        form.Add(file, "photo", "answer.jpg");
        var response = await harness.Client.PostAsync($"/api/rooms/{player.RoomCode}/questions/{questionInstanceId}/photo-answers", form);
        response.EnsureSuccessStatusCode();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
