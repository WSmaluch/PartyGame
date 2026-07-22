using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Api;

public sealed class DrawingAnswerMixedGameE2ETests
{
    [Fact]
    public async Task RealHostAndSignalR_RunExactOneOneOneOnePlanToCompleted()
    {
        // This test advances the clock explicitly. Keep the hosted worker out of that
        // deterministic transition loop so it cannot process the same expiry concurrently.
        await using var harness = new PhotoAnswerTestHarness(settings: new Dictionary<string, string?>
        {
            ["GameFlow:WorkerIntervalMilliseconds"] = "60000"
        });
        var host = await Create(harness); var second = await DrawingAnswerGameE2ETests.Join(harness, host.RoomCode, "Second"); var third = await DrawingAnswerGameE2ETests.Join(harness, host.RoomCode, "Third"); var players = new[] { host, second, third };
        await using var display = DrawingAnswerGameE2ETests.Connection(harness); await using var c1 = DrawingAnswerGameE2ETests.Connection(harness); await using var c2 = DrawingAnswerGameE2ETests.Connection(harness); await using var c3 = DrawingAnswerGameE2ETests.Connection(harness); var connections = new[] { c1, c2, c3 }; await Task.WhenAll(connections.Append(display).Select(c => c.StartAsync())); await display.InvokeAsync<RoomSnapshot>("AttachDisplay", host.RoomCode); await Task.WhenAll(players.Zip(connections).Select(x => x.Second.InvokeAsync<RoomSnapshot>("AttachPlayer", x.First.RoomCode, x.First.PlayerId, x.First.ReconnectToken))); await Task.WhenAll(players.Select(p => DrawingAnswerGameE2ETests.UploadProfile(harness, p))); await Task.WhenAll(players.Zip(connections).Select(x => x.Second.InvokeAsync<RoomSnapshot>("SetReady", x.First.RoomCode, x.First.PlayerId, x.First.ReconnectToken, true)));
        var seen = new Dictionary<QuestionType, int>(); var handled = new HashSet<(Guid, GameStage)>(); var png = await PhotoAnswerTestHarness.DrawingAsync(); var jpeg = await PhotoAnswerTestHarness.ImageAsync();
        for (var iteration = 0; iteration < 100; iteration++)
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>(); var session = await db.GameSessions.SingleAsync(); if (session.Stage == GameStage.Completed) break;
            if (session.CurrentQuestionInstanceId is Guid id)
            {
                var type = await db.GameQuestionInstances.Where(i => i.Id == id).Select(i => i.Question.Type).SingleAsync(); if (!seen.ContainsKey(type)) seen[type] = 1;
                if (session.Stage == GameStage.CollectingPlayerSelections && handled.Add((id, session.Stage))) { for (var i = 0; i < 3; i++) await connections[i].InvokeAsync("SubmitPlayerSelection", host.RoomCode, players[i].PlayerId, players[i].ReconnectToken, players[(i + 1) % 3].PlayerId); continue; }
                if (session.Stage == GameStage.CollectingTextAnswers && handled.Add((id, session.Stage))) { var eligible = await db.TextAnswerEligiblePlayers.Where(e => e.QuestionInstanceId == id).Select(e => e.PlayerId).ToListAsync(); for (var i = 0; i < 3; i++) if (eligible.Contains(players[i].PlayerId)) await connections[i].InvokeAsync("SubmitTextAnswer", host.RoomCode, players[i].PlayerId, players[i].ReconnectToken, $"Answer {i}"); continue; }
                if (session.Stage == GameStage.CollectingTextAnswerVotes && handled.Add((id, session.Stage))) { var answers = await db.TextAnswerSubmissions.Where(a => a.QuestionInstanceId == id).Select(a => new { a.Id, a.AuthorPlayerId }).ToListAsync(); for (var i = 0; i < 3; i++) await connections[i].InvokeAsync("SubmitTextAnswerVote", host.RoomCode, players[i].PlayerId, players[i].ReconnectToken, answers.First(a => a.AuthorPlayerId != players[i].PlayerId).Id); continue; }
                if (session.Stage == GameStage.CollectingPhotoAnswers && handled.Add((id, session.Stage))) { foreach (var player in players) (await UploadPhoto(harness, player, id, jpeg)).EnsureSuccessStatusCode(); continue; }
                if (session.Stage == GameStage.CollectingPhotoAnswerVotes && handled.Add((id, session.Stage))) { var answers = await db.PhotoAnswerSubmissions.Where(a => a.QuestionInstanceId == id).Select(a => a.Id).ToListAsync(); for (var i = 0; i < 3; i++) await connections[i].InvokeAsync("SubmitPhotoAnswerVote", host.RoomCode, players[i].PlayerId, players[i].ReconnectToken, id, answers[i]); continue; }
                if (session.Stage == GameStage.CollectingDrawingAnswers && handled.Add((id, session.Stage))) { var before = await harness.Client.GetStringAsync($"/api/rooms/{host.RoomCode}"); Assert.DoesNotContain("displayDrawingUrl", before); foreach (var player in players) (await DrawingAnswerGameE2ETests.UploadDrawing(harness, player, id, png)).EnsureSuccessStatusCode(); continue; }
                if (session.Stage == GameStage.CollectingDrawingAnswerVotes && handled.Add((id, session.Stage))) { var answers = await db.DrawingAnswerSubmissions.Where(a => a.QuestionInstanceId == id).Select(a => a.Id).ToListAsync(); for (var i = 0; i < 3; i++) await connections[i].InvokeAsync("SubmitDrawingAnswerVote", host.RoomCode, players[i].PlayerId, players[i].ReconnectToken, id, answers[i]); continue; }
            }
            // SignalR/REST actions above use independent DbContexts. Do not force a timer
            // transition using an entity tracked before one of those actions: it can replay a
            // previous stage and recreate its eligible-player rows.
            db.ChangeTracker.Clear();
            session = await db.GameSessions.SingleAsync();
            session.StageEndsAtUtc = DateTimeOffset.UtcNow.AddMilliseconds(-1); await db.SaveChangesAsync(); var machine = scope.ServiceProvider.GetRequiredService<GameStateMachine>(); if (await machine.ProcessTransitionAsync(session.Id, DateTimeOffset.UtcNow, default)) { var room = await db.GameRooms.SingleAsync(); room.PublicStateChanged(DateTimeOffset.UtcNow); await db.SaveChangesAsync(); }
        }
        await using (var scope = harness.Factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>(); Assert.Equal(GameStage.Completed, await db.GameSessions.Select(s => s.Stage).SingleAsync()); Assert.Equal(4, seen.Count); Assert.All(seen.Values, count => Assert.Equal(1, count)); var reasons = await db.ScoreTransactions.Select(t => t.Reason).Distinct().ToListAsync(); Assert.Contains("Player Selection Score", reasons); Assert.Contains("Text Answer Score", reasons); Assert.Contains("PhotoAnswerConformity", reasons); Assert.Contains("DrawingAnswerConformity", reasons); Assert.True(await db.Players.SumAsync(p => p.Score) > 0); }
        var publicJson = await harness.Client.GetStringAsync($"/api/rooms/{host.RoomCode}"); Assert.DoesNotContain("storageKey", publicJson, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("reconnectToken", publicJson, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<RoomAccessResponse> Create(PhotoAnswerTestHarness harness) { var response = await harness.Client.PostAsJsonAsync("/api/rooms", new CreateRoomRequest("Host", new RoomSettingsRequest(1, 4, 5, 5, 5, 10, 30, 3, false, 1), ["starter"], ["PlayerSelection", "TextAnswer", "PhotoAnswer", "DrawingAnswer"])); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!; }
    private static async Task<HttpResponseMessage> UploadPhoto(PhotoAnswerTestHarness harness, RoomAccessResponse player, Guid id, byte[] jpeg) { using var form = new MultipartFormDataContent(); form.Add(new StringContent(player.PlayerId.ToString()), "playerId"); form.Add(new StringContent(player.ReconnectToken), "reconnectToken"); form.Add(new StringContent(Guid.NewGuid().ToString()), "clientSubmissionId"); var file = new ByteArrayContent(jpeg); file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg"); form.Add(file, "photo", "photo.jpg"); return await harness.Client.PostAsync($"/api/rooms/{player.RoomCode}/questions/{id}/photo-answers", form); }
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
