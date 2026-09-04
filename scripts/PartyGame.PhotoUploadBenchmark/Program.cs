using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

var baseUrl = Environment.GetEnvironmentVariable("PARTYGAME_BENCHMARK_URL") ?? "http://127.0.0.1:5056";
using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

// This exact byte sequence is reused for every answer. It is deliberately
// generated once, then held in memory: source dimensions and encoding cannot
// explain a difference between checkpoints.
var photo = await FixedPhoto();
const int width = 640;
const int height = 480;
var created = await PostJson("/api/rooms", new
{
    nickname = "Bench A",
    enabledQuestionTypes = new[] { "PhotoAnswer" },
    settings = new { roundCount = 1, questionsPerRound = 5, votingSeconds = 5, photoSeconds = 10, resultPresentationSeconds = 3, finalRoundEnabled = false, finalDrawingPasses = 1 }
});
var roomCode = created.GetProperty("roomCode").GetString()!;
var players = new List<PlayerAccess> { Access(created, "Bench A") };
players.Add(Access(await PostJson($"/api/rooms/{roomCode}/players", new { nickname = "Bench B" }), "Bench B"));
players.Add(Access(await PostJson($"/api/rooms/{roomCode}/players", new { nickname = "Bench C" }), "Bench C"));

await using var display = new HubConnectionBuilder().WithUrl($"{baseUrl}/hubs/game").Build();
await display.StartAsync();
await display.InvokeAsync("AttachDisplay", roomCode);
var connections = new List<HubConnection>();
foreach (var player in players)
{
    var connection = new HubConnectionBuilder().WithUrl($"{baseUrl}/hubs/game").Build();
    await connection.StartAsync();
    await connection.InvokeAsync("AttachPlayer", roomCode, player.Id, player.Token);
    await UploadProfile(player, photo);
    await connection.InvokeAsync("SetReady", roomCode, player.Id, player.Token, true);
    connections.Add(connection);
}

var samples = new List<Sample>();
for (var question = 1; question <= 5; question++)
{
    var collecting = await WaitForStage("CollectingPhotoAnswers", TimeSpan.FromSeconds(40));
    var questionId = collecting.GetProperty("game").GetProperty("photoAnswerResults").GetProperty("questionInstanceId").GetGuid();
    foreach (var player in players)
        samples.Add(await UploadAnswer(player, questionId, photo, samples.Count + 1));

    var voting = await WaitForStage("CollectingPhotoAnswerVotes", TimeSpan.FromSeconds(15));
    var option = voting.GetProperty("game").GetProperty("photoAnswerResults").GetProperty("anonymousOptions")[0].GetProperty("photoAnswerId").GetGuid();
    foreach (var connection in connections)
    {
        var player = players[connections.IndexOf(connection)];
        await connection.InvokeAsync("SubmitPhotoAnswerVote", roomCode, player.Id, player.Token, questionId, option);
    }
    await WaitForStage("ShowingPhotoAnswerResults", TimeSpan.FromSeconds(10));
}
await WaitForStage("Completed", TimeSpan.FromSeconds(30));

var audit = await SubmissionAudit(players[0]);
var accepted = audit.GetProperty("entries").EnumerateArray().Count(entry =>
    entry.GetProperty("actionType").GetString() == "PhotoAnswer" && entry.GetProperty("result").GetString() == "Accepted");
if (accepted != 15) Fail($"Expected 15 accepted photo uploads, got {accepted}.");
var checkpoints = new[] { 1, 5, 10, 15 }.Select(index => samples.Single(sample => sample.Upload == index)).ToArray();
Console.WriteLine(JsonSerializer.Serialize(new
{
    kind = "PartyGamePhotoUploadBenchmark",
    image = new { format = "image/jpeg", width, height, encodedBytes = photo.Length },
    acceptedUploads = accepted,
    checkpoints = checkpoints.Select(sample => new { sample.Upload, sample.ClientRequestMs, sample.ServerEndpointMs, sample.ServiceMs, sample.NotifyMs, sample.PrivateStateMs, sample.SnapshotMs, sample.AckBytes }),
    medians = new
    {
        upload1 = Median(samples.Where(sample => sample.Upload is >= 1 and <= 3).Select(sample => sample.ClientRequestMs)),
        upload5 = Median(samples.Where(sample => sample.Upload is >= 4 and <= 6).Select(sample => sample.ClientRequestMs)),
        upload10 = Median(samples.Where(sample => sample.Upload is >= 9 and <= 11).Select(sample => sample.ClientRequestMs)),
        upload15 = Median(samples.Where(sample => sample.Upload is >= 13 and <= 15).Select(sample => sample.ClientRequestMs))
    },
    ratio15To1 = checkpoints[3].ClientRequestMs / Math.Max(0.01, checkpoints[0].ClientRequestMs),
    historicalMediaEnumeration = "none in the media storage write path; room metadata is loaded for authorization and state mutation",
    snapshotsContainImageBytes = false
}));
foreach (var connection in connections) await connection.DisposeAsync();

async Task<JsonElement> PostJson(string path, object body)
{
    using var response = await http.PostAsJsonAsync(path, body, json);
    return await ReadSuccess(response);
}

async Task UploadProfile(PlayerAccess player, byte[] content)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{player.Id}/profile-photo");
    request.Headers.Add("X-Player-Token", player.Token);
    var file = new ByteArrayContent(content); file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
    request.Content = new MultipartFormDataContent { { file, "file", "profile.jpg" } };
    using var response = await http.SendAsync(request); _ = await ReadSuccess(response);
}

async Task<Sample> UploadAnswer(PlayerAccess player, Guid questionId, byte[] content, int upload)
{
    using var form = new MultipartFormDataContent();
    form.Add(new StringContent(player.Id.ToString()), "playerId");
    form.Add(new StringContent(player.Token), "reconnectToken");
    form.Add(new StringContent(Guid.NewGuid().ToString()), "clientSubmissionId");
    var file = new ByteArrayContent(content); file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
    form.Add(file, "photo", "benchmark.jpg");
    using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/questions/{questionId}/photo-answers") { Content = form };
    request.Headers.Add("X-PartyGame-Benchmark", "photo-upload");
    var watch = Stopwatch.StartNew();
    using var response = await http.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    watch.Stop();
    if (!response.IsSuccessStatusCode) Fail($"Photo upload {upload} failed: HTTP {(int)response.StatusCode}.");
    using var parsed = JsonDocument.Parse(body);
    if (!parsed.RootElement.GetProperty("playerPrivateGameState").GetProperty("hasSubmittedPhotoAnswer").GetBoolean()) Fail($"Photo upload {upload} has no private acknowledgement.");
    var server = response.Headers.TryGetValues("Server-Timing", out var timings) ? ParseServerTiming(timings.Single()) : throw new InvalidOperationException("Benchmark Server-Timing header is missing.");
    return new Sample(upload, watch.Elapsed.TotalMilliseconds, server["partygame"], server["service"], server["notify"], server["private"], server["snapshot"], System.Text.Encoding.UTF8.GetByteCount(body));
}

async Task<JsonElement> SubmissionAudit(PlayerAccess player)
{
    var url = $"/api/rooms/{roomCode}/submission-audit?playerId={player.Id}&reconnectToken={Uri.EscapeDataString(player.Token)}";
    using var response = await http.GetAsync(url); return await ReadSuccess(response);
}

async Task<JsonElement> GetRoom()
{
    using var response = await http.GetAsync($"/api/rooms/{roomCode}"); return await ReadSuccess(response);
}

async Task<JsonElement> WaitForStage(string stage, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var room = await GetRoom();
        if (room.TryGetProperty("game", out var game) && game.ValueKind != JsonValueKind.Null && game.GetProperty("stage").GetString() == stage) return room;
        await Task.Delay(100);
    }
    Fail($"Timed out waiting for {stage}."); return default;
}

static async Task<byte[]> FixedPhoto()
{
    using var image = new Image<Rgba32>(640, 480, Color.CornflowerBlue);
    image[320, 240] = Color.White;
    await using var stream = new MemoryStream();
    await image.SaveAsync(stream, new JpegEncoder { Quality = 86 });
    return stream.ToArray();
}

static async Task<JsonElement> ReadSuccess(HttpResponseMessage response)
{
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) Fail($"HTTP {(int)response.StatusCode}.");
    return JsonDocument.Parse(text).RootElement.Clone();
}

static Dictionary<string, double> ParseServerTiming(string value)
{
    var result = new Dictionary<string, double>(StringComparer.Ordinal);
    foreach (var metric in value.Split(',', StringSplitOptions.TrimEntries))
    {
        var parts = metric.Split(";dur=", StringSplitOptions.TrimEntries);
        if (parts.Length == 2) result[parts[0]] = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
    }
    foreach (var metric in new[] { "partygame", "service", "notify", "private", "snapshot" })
        if (!result.ContainsKey(metric)) throw new InvalidOperationException("Malformed Server-Timing header.");
    return result;
}
static double Median(IEnumerable<double> values) { var sorted = values.Order().ToArray(); return sorted[sorted.Length / 2]; }
static PlayerAccess Access(JsonElement value, string name) => new(value.GetProperty("playerId").GetGuid(), value.GetProperty("reconnectToken").GetString()!, name);
static void Fail(string message) => throw new InvalidOperationException(message);
internal sealed record PlayerAccess(Guid Id, string Token, string Name);
internal sealed record Sample(int Upload, double ClientRequestMs, double ServerEndpointMs, double ServiceMs, double NotifyMs, double PrivateStateMs, double SnapshotMs, int AckBytes);
