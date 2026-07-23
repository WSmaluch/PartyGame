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

public sealed class DrawingAnswerGameE2ETests
{
    [Fact]
    public async Task RealHostMultipartAndSignalR_RunFourDrawingQuestionsToCompleted()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var host = await Create(harness, "Host"); var second = await Join(harness, host.RoomCode, "Second"); var third = await Join(harness, host.RoomCode, "Third"); var players = new[] { host, second, third };
        await using var display = Connection(harness); await using var c1 = Connection(harness); await using var c2 = Connection(harness); await using var c3 = Connection(harness); var connections = new[] { c1, c2, c3 };
        await Task.WhenAll(connections.Append(display).Select(c => c.StartAsync())); await display.InvokeAsync<RoomSnapshot>("AttachDisplay", host.RoomCode); await Task.WhenAll(players.Zip(connections).Select(x => x.Second.InvokeAsync<RoomSnapshot>("AttachPlayer", x.First.RoomCode, x.First.PlayerId, x.First.ReconnectToken))); await Task.WhenAll(players.Select(p => UploadProfile(harness, p))); await Task.WhenAll(players.Zip(connections).Select(x => x.Second.InvokeAsync<RoomSnapshot>("SetReady", x.First.RoomCode, x.First.PlayerId, x.First.ReconnectToken, true)));

        var seenInstances = new HashSet<Guid>(); var seenKeys = new HashSet<string>(); var seenStages = new HashSet<GameStage>(); var handled = new HashSet<(Guid, GameStage)>(); var png = await PhotoAnswerTestHarness.DrawingAsync(); var selfVoteSeen = false;
        for (var iteration = 0; iteration < 80; iteration++)
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>(); var session = await db.GameSessions.SingleAsync(); seenStages.Add(session.Stage); if (session.Stage == GameStage.Completed) break;
            if (session.CurrentQuestionInstanceId is Guid instanceId)
            {
                var definition = await db.GameQuestionInstances.Where(i => i.Id == instanceId).Select(i => new { i.Question.Key, i.Question.Type }).SingleAsync(); seenInstances.Add(instanceId); seenKeys.Add(definition.Key); Assert.Equal(QuestionType.DrawingAnswer, definition.Type);
                if (session.Stage == GameStage.CollectingDrawingAnswers && handled.Add((instanceId, session.Stage)))
                {
                    var publicJson = await harness.Client.GetStringAsync($"/api/rooms/{host.RoomCode}"); Assert.DoesNotContain("displayDrawingUrl", publicJson);
                    foreach (var player in players) { var response = await UploadDrawing(harness, player, instanceId, png); response.EnsureSuccessStatusCode(); var body = await response.Content.ReadAsStringAsync(); Assert.Contains("ownDrawingAnswerId", body); }
                    continue;
                }
                if (session.Stage == GameStage.RevealingDrawingAnswers && handled.Add((instanceId, session.Stage)))
                {
                    var json = await harness.Client.GetStringAsync($"/api/rooms/{host.RoomCode}"); Assert.Contains("displayDrawingUrl", json); Assert.DoesNotContain("authorNickname", json); var assets = await db.DrawingAnswerSubmissions.Where(s => s.QuestionInstanceId == instanceId).Select(s => s.MediaAssetId).ToListAsync(); foreach (var asset in assets) Assert.True((await harness.Client.GetAsync($"/api/media/{asset}/display")).IsSuccessStatusCode);
                }
                if (session.Stage == GameStage.CollectingDrawingAnswerVotes && handled.Add((instanceId, session.Stage)))
                {
                    var answers = await db.DrawingAnswerSubmissions.Where(s => s.QuestionInstanceId == instanceId).Select(s => new { s.Id, s.AuthorPlayerId }).ToListAsync();
                    for (var index = 0; index < players.Length; index++) { var selected = index == 0 ? answers.Single(a => a.AuthorPlayerId == players[index].PlayerId).Id : answers[(index + 1) % answers.Count].Id; if (index == 0) selfVoteSeen = true; await connections[index].InvokeAsync("SubmitDrawingAnswerVote", players[index].RoomCode, players[index].PlayerId, players[index].ReconnectToken, instanceId, selected); }
                    continue;
                }
                if (session.Stage == GameStage.ShowingDrawingAnswerResults && handled.Add((instanceId, session.Stage)))
                {
                    var json = await harness.Client.GetStringAsync($"/api/rooms/{host.RoomCode}"); Assert.Contains("authorNickname", json); Assert.Contains("pointsAwarded", json);
                }
            }
            session.StageEndsAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-1); await db.SaveChangesAsync(); var machine = scope.ServiceProvider.GetRequiredService<GameStateMachine>(); if (await machine.ProcessTransitionAsync(session.Id, DateTimeOffset.UtcNow, default)) { var room = await db.GameRooms.SingleAsync(); room.PublicStateChanged(DateTimeOffset.UtcNow); await db.SaveChangesAsync(); }
        }
        await using (var scope = harness.Factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>(); Assert.Equal(GameStage.Completed, await db.GameSessions.Select(s => s.Stage).SingleAsync()); Assert.Equal(4, seenInstances.Count); Assert.Equal(4, seenKeys.Count); Assert.Equal(12, await db.DrawingAnswerSubmissions.CountAsync()); Assert.Equal(12, await db.DrawingAnswerVotes.CountAsync()); Assert.All(await db.ScoreTransactions.ToListAsync(), t => Assert.Equal("DrawingAnswerConformity", t.Reason)); }
        Assert.True(selfVoteSeen); Assert.Contains(GameStage.RoundSummary, seenStages); Assert.Contains(GameStage.Completed, seenStages);
    }

    internal static HubConnection Connection(PhotoAnswerTestHarness harness) => new HubConnectionBuilder().WithUrl("http://localhost/hubs/game", options => { options.Transports = HttpTransportType.LongPolling; options.HttpMessageHandlerFactory = _ => harness.Factory.Server.CreateHandler(); }).AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter())).Build();
    internal static async Task<RoomAccessResponse> Create(PhotoAnswerTestHarness harness, string nickname) { var response = await harness.Client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest(nickname, new RoomSettingsRequest(1, 4, 5, 5, 5, 10, 30, 3, false, 1), ["starter"], ["DrawingAnswer"])); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!; }
    internal static async Task<RoomAccessResponse> Join(PhotoAnswerTestHarness harness, string code, string nickname) { var response = await harness.Client.PostAsJsonAsync($"/api/rooms/{code}/players", new JoinRoomRequest(nickname)); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!; }
    internal static async Task UploadProfile(PhotoAnswerTestHarness harness, RoomAccessResponse player) { using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{player.RoomCode}/players/{player.PlayerId}/profile-photo"); request.Headers.Add("X-Player-Token", player.ReconnectToken); var file = new ByteArrayContent(await PhotoAnswerTestHarness.ImageAsync()); file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg"); request.Content = new MultipartFormDataContent { { file, "file", "profile.jpg" } }; (await harness.Client.SendAsync(request)).EnsureSuccessStatusCode(); }
    internal static async Task<HttpResponseMessage> UploadDrawing(PhotoAnswerTestHarness harness, RoomAccessResponse player, Guid instanceId, byte[] png) { using var form = new MultipartFormDataContent(); form.Add(new StringContent(player.PlayerId.ToString()), "playerId"); form.Add(new StringContent(player.ReconnectToken), "reconnectToken"); form.Add(new StringContent(Guid.NewGuid().ToString()), "clientSubmissionId"); var file = new ByteArrayContent(png); file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png"); form.Add(file, "drawing", "drawing.png"); return await harness.Client.PostAsync($"/api/rooms/{player.RoomCode}/questions/{instanceId}/drawing-answers", form); }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
