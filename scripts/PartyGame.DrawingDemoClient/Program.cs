using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

var baseUrl = Environment.GetEnvironmentVariable("PARTYGAME_DEMO_URL") ?? "http://127.0.0.1:5004";
using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var created = await PostJson("/api/rooms", new
{
    nickname = "Host",
    enabledQuestionTypes = new[] { "DrawingAnswer" },
    settings = new { roundCount = 1, questionsPerRound = 4, drawingSeconds = 30, votingSeconds = 10 },
});
var roomCode = created.GetProperty("roomCode").GetString()!;
var observationDelay = int.TryParse(Environment.GetEnvironmentVariable("PARTYGAME_DEMO_OBSERVATION_DELAY_MS"), out var delay) ? delay : 0;
var players = new List<PlayerAccess> { Access(created, "Host") };
players.Add(Access(await PostJson($"/api/rooms/{roomCode}/players", new { nickname = "Wojtek" }), "Wojtek"));
players.Add(Access(await PostJson($"/api/rooms/{roomCode}/players", new { nickname = "Kasia" }), "Kasia"));
var externalPlayerFile = Environment.GetEnvironmentVariable("PARTYGAME_EXTERNAL_PLAYER_FILE");
var externalHost = !string.IsNullOrWhiteSpace(externalPlayerFile);
if (externalHost)
{
    var access = JsonSerializer.Serialize(new { roomCode, playerId = players[0].Id, reconnectToken = players[0].Token, nickname = players[0].Name });
    await File.WriteAllTextAsync(externalPlayerFile!, access);
}

var displayReceivedPrivateState = false;
await using var display = Connection();
display.On<JsonElement>("PlayerPrivateGameStateUpdated", _ => displayReceivedPrivateState = true);
await display.StartAsync();
await display.InvokeAsync("AttachDisplay", roomCode);
var connections = players.Select(_ => Connection()).ToArray();
await Task.WhenAll(connections.Select(connection => connection.StartAsync()));
for (var index = 0; index < players.Count; index++)
{
    await connections[index].InvokeAsync("AttachPlayer", roomCode, players[index].Id, players[index].Token);
    await UploadProfile(players[index], await ProfileImage(index));
    await connections[index].InvokeAsync("SetReady", roomCode, players[index].Id, players[index].Token, true);
}

var seenQuestions = new HashSet<Guid>();
var validDrawing = await Drawing(blank: false);
var blankDrawing = await Drawing(blank: true);
for (var questionNumber = 1; questionNumber <= 4; questionNumber++)
{
    var collecting = await WaitForStage("CollectingDrawingAnswers", TimeSpan.FromSeconds(30));
    var game = collecting.GetProperty("game");
    var definitionId = game.GetProperty("question").GetProperty("id").GetGuid();
    if (!seenQuestions.Add(definitionId)) Fail("Question repeated.");
    var questionId = game.GetProperty("drawingAnswerResults").GetProperty("questionInstanceId").GetGuid();
    AssertPublicSafety(collecting, collecting: true);
    await Observe();

    var blank = await Upload(externalHost ? players[1] : players[0], questionId, Guid.NewGuid(), blankDrawing);
    if (blank.IsSuccessStatusCode || !await HasErrorCode(blank, "drawing_answer_blank")) Fail("Blank canvas was accepted.");

    var submitted = new List<(Guid ClientId, Guid AnswerId)>();
    if (externalHost) await WaitForDrawingProgress("submittedPlayers", 1, TimeSpan.FromSeconds(180));
    foreach (var player in players.Skip(externalHost ? 1 : 0))
    {
        var clientId = Guid.NewGuid();
        var response = await Upload(player, questionId, clientId, validDrawing);
        var body = await ReadSuccess(response);
        var answerId = body.GetProperty("drawingAnswerId").GetGuid();
        var privateState = body.GetProperty("playerPrivateGameState");
        if (!privateState.GetProperty("hasSubmittedDrawingAnswer").GetBoolean() ||
            privateState.GetProperty("ownDrawingAnswerId").GetGuid() != answerId) Fail("Private drawing state is invalid.");
        submitted.Add((clientId, answerId));
    }

    var retry = await ReadSuccess(await Upload(players[^1], questionId, submitted[^1].ClientId, validDrawing));
    if (retry.GetProperty("drawingAnswerId").GetGuid() != submitted[^1].AnswerId) Fail("Retry created a duplicate drawing.");

    var reveal = await WaitForStage("RevealingDrawingAnswers", TimeSpan.FromSeconds(10));
    AssertPublicSafety(reveal, collecting: false);
    var revealOptions = reveal.GetProperty("game").GetProperty("drawingAnswerResults").GetProperty("anonymousOptions").EnumerateArray().ToList();
    if (revealOptions.Count != 3) Fail("Reveal does not contain three drawings.");
    var revealOrder = revealOptions.Select(option => option.GetProperty("revealOrder").GetInt32()).ToArray();
    foreach (var option in revealOptions)
    {
        foreach (var property in new[] { "displayDrawingUrl", "thumbnailDrawingUrl" })
        {
            var media = await http.GetAsync(option.GetProperty(property).GetString());
            if (!media.IsSuccessStatusCode || media.Content.Headers.ContentType?.MediaType != "image/png") Fail("PNG media URL is unavailable.");
        }
    }
    Console.WriteLine($"Question {questionNumber}: anonymous reveal and PNG URLs verified.");
    await Observe();

    var voting = await WaitForStage("CollectingDrawingAnswerVotes", TimeSpan.FromSeconds(10));
    AssertPublicSafety(voting, collecting: false);
    var votingOptions = voting.GetProperty("game").GetProperty("drawingAnswerResults").GetProperty("anonymousOptions").EnumerateArray().ToList();
    var votingOrder = votingOptions.Select(option => option.GetProperty("displayOrder").GetInt32()).Order().ToArray();
    if (!revealOrder.Order().SequenceEqual(votingOrder)) Fail("RevealOrder changed before voting.");
    await Observe();
    var hostDrawing = submitted[0].AnswerId;
    if (externalHost) await WaitForDrawingProgress("votedPlayers", 1, TimeSpan.FromSeconds(180));
    for (var index = externalHost ? 1 : 0; index < players.Count; index++)
        await connections[index].InvokeAsync("SubmitDrawingAnswerVote", roomCode, players[index].Id, players[index].Token, questionId, hostDrawing);

    var results = await WaitForStage("ShowingDrawingAnswerResults", TimeSpan.FromSeconds(10));
    var options = results.GetProperty("game").GetProperty("drawingAnswerResults").GetProperty("options").EnumerateArray().ToList();
    if (options.Any(option => !option.TryGetProperty("authorPlayerId", out _))) Fail("Authors were not revealed in results.");
    var winner = options.Single(option => option.GetProperty("drawingAnswerId").GetGuid() == hostDrawing);
    if (options.Sum(option => option.GetProperty("voteCount").GetInt32()) != 3) Fail("Vote count is invalid.");
    if (options.SelectMany(option => option.GetProperty("voters").EnumerateArray()).Any(voter => voter.GetProperty("pointsAwarded").GetInt32() <= 0))
        Fail("Scoring contains an author bonus or an invalid voter award.");
    Console.WriteLine("Self-vote, author reveal and voter-only scoring verified.");
    await Observe();
}

await WaitForStage("Completed", TimeSpan.FromSeconds(30));
if (displayReceivedPrivateState) Fail("Display received private player state.");
Console.WriteLine("SUCCESS: DrawingAnswer game reached Completed with four unique questions.");
foreach (var connection in connections) await connection.DisposeAsync();

HubConnection Connection() => new HubConnectionBuilder().WithUrl($"{baseUrl}/hubs/game").Build();

async Task Observe()
{
    if (observationDelay > 0) await Task.Delay(observationDelay);
}

async Task WaitForDrawingProgress(string property, int expected, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var room = await GetRoom();
        if (room.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Object &&
            game.TryGetProperty("drawingAnswerResults", out var results) && results.ValueKind == JsonValueKind.Object &&
            results.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() >= expected) return;
        await Task.Delay(100);
    }
    Fail($"Timed out waiting for DrawingAnswer progress {property}={expected}.");
}

async Task<JsonElement> PostJson(string path, object body)
{
    var response = await http.PostAsJsonAsync(path, body, json);
    return await ReadSuccess(response);
}

async Task<JsonElement> ReadSuccess(HttpResponseMessage response)
{
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) Fail($"HTTP {(int)response.StatusCode}: {text}");
    return JsonDocument.Parse(text).RootElement.Clone();
}

async Task<bool> HasErrorCode(HttpResponseMessage response, string code) =>
    (await response.Content.ReadAsStringAsync()).Contains($"\"code\":\"{code}\"", StringComparison.Ordinal);

async Task UploadProfile(PlayerAccess player, byte[] bytes)
{
    using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{player.Id}/profile-photo");
    request.Headers.Add("X-Player-Token", player.Token);
    var file = new ByteArrayContent(bytes); file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
    request.Content = new MultipartFormDataContent { { file, "file", "profile.jpg" } };
    (await http.SendAsync(request)).EnsureSuccessStatusCode();
}

async Task<HttpResponseMessage> Upload(PlayerAccess player, Guid questionId, Guid clientId, byte[] bytes)
{
    using var form = new MultipartFormDataContent();
    form.Add(new StringContent(player.Id.ToString()), "playerId");
    form.Add(new StringContent(player.Token), "reconnectToken");
    form.Add(new StringContent(clientId.ToString()), "clientSubmissionId");
    var file = new ByteArrayContent(bytes); file.Headers.ContentType = MediaTypeHeaderValue.Parse("image/png");
    form.Add(file, "drawing", "ignored-client-name.png");
    return await http.PostAsync($"/api/rooms/{roomCode}/questions/{questionId}/drawing-answers", form);
}

async Task<JsonElement> GetRoom() => await ReadSuccess(await http.GetAsync($"/api/rooms/{roomCode}"));

async Task<JsonElement> WaitForStage(string expected, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var room = await GetRoom();
        if (room.TryGetProperty("game", out var game) && game.ValueKind != JsonValueKind.Null &&
            game.GetProperty("stage").GetString() == expected) return room;
        await Task.Delay(100);
    }
    Fail($"Timed out waiting for {expected}.");
    return default;
}

static void AssertPublicSafety(JsonElement snapshot, bool collecting)
{
    var text = snapshot.GetRawText();
    foreach (var forbidden in new[] { "storageKey", "mediaAssetId", "ownDrawingAnswerId" })
        if (text.Contains(forbidden, StringComparison.OrdinalIgnoreCase)) Fail($"Public snapshot leaked {forbidden}.");
    if (collecting && text.Contains("displayDrawingUrl", StringComparison.OrdinalIgnoreCase)) Fail("Drawing URL leaked while collecting.");
    if (!collecting && text.Contains("authorNickname", StringComparison.OrdinalIgnoreCase)) Fail("Author leaked before results.");
}

static PlayerAccess Access(JsonElement element, string name) =>
    new(element.GetProperty("playerId").GetGuid(), element.GetProperty("reconnectToken").GetString()!, name);

static async Task<byte[]> Drawing(bool blank)
{
    using var image = new Image<Rgba32>(640, 480, Color.White);
    if (!blank) for (var y = 0; y < image.Height; y++) { image[319, y] = Color.Blue; image[320, y] = Color.Blue; }
    await using var stream = new MemoryStream(); await image.SaveAsync(stream, new PngEncoder()); return stream.ToArray();
}

static async Task<byte[]> ProfileImage(int index)
{
    using var image = new Image<Rgba32>(400, 400, index == 0 ? Color.Blue : index == 1 ? Color.Green : Color.Orange);
    await using var stream = new MemoryStream(); await image.SaveAsync(stream, new JpegEncoder()); return stream.ToArray();
}

static void Fail(string message) => throw new InvalidOperationException(message);
internal sealed record PlayerAccess(Guid Id, string Token, string Name);
