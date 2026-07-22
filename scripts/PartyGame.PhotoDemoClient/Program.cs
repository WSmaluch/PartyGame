using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

var baseUrl = Environment.GetEnvironmentVariable("PARTYGAME_DEMO_URL") ?? "http://127.0.0.1:5003";
using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);

var create = await PostJson("/api/rooms", new
{
    nickname = "Host", enabledQuestionTypes = new[] { "PhotoAnswer" },
    settings = new { roundCount = 1, questionsPerRound = 4, votingSeconds = 5 }
});
var roomCode = create.GetProperty("roomCode").GetString()!;
var observationDelay = int.TryParse(Environment.GetEnvironmentVariable("PARTYGAME_DEMO_OBSERVATION_DELAY_MS"), out var delay) ? delay : 0;
var initialObservationDelay = int.TryParse(Environment.GetEnvironmentVariable("PARTYGAME_DEMO_INITIAL_DELAY_MS"), out var initialDelay) ? initialDelay : observationDelay;
Console.WriteLine($"Room code: {roomCode}");
await Observe(initialObservationDelay);
var players = new List<PlayerAccess> { Access(create, "Host") };
players.Add(Access(await PostJson($"/api/rooms/{roomCode}/players", new { nickname = "Wojtek" }), "Wojtek"));
players.Add(Access(await PostJson($"/api/rooms/{roomCode}/players", new { nickname = "Kasia" }), "Kasia"));
var externalPlayerFile = Environment.GetEnvironmentVariable("PARTYGAME_EXTERNAL_PLAYER_FILE");
var externalHost = !string.IsNullOrWhiteSpace(externalPlayerFile);
if (externalHost)
{
    var access = JsonSerializer.Serialize(new { roomCode, playerId = players[0].Id, reconnectToken = players[0].Token, nickname = players[0].Name });
    await File.WriteAllTextAsync(externalPlayerFile!, access);
}

await using var display = new HubConnectionBuilder().WithUrl($"{baseUrl}/hubs/game").Build();
await display.StartAsync();
await display.InvokeAsync("AttachDisplay", roomCode);
var playerConnections = new List<HubConnection>();
foreach (var player in players)
{
    var connection = new HubConnectionBuilder().WithUrl($"{baseUrl}/hubs/game").Build();
    await connection.StartAsync();
    await connection.InvokeAsync("AttachPlayer", roomCode, player.Id, player.Token);
    playerConnections.Add(connection);
    await UploadProfile(player, await CreateImage(player.Name == "Host" ? Color.CornflowerBlue : player.Name == "Wojtek" ? Color.Orange : Color.MediumSeaGreen));
    await connection.InvokeAsync("SetReady", roomCode, player.Id, player.Token, true);
}

var seenQuestions = new HashSet<Guid>();
for (var questionNumber = 1; questionNumber <= 4; questionNumber++)
{
    var collecting = await WaitForStage("CollectingPhotoAnswers", TimeSpan.FromSeconds(30));
    var game = collecting.GetProperty("game");
    var questionDefinitionId = game.GetProperty("question").GetProperty("id").GetGuid();
    if (!seenQuestions.Add(questionDefinitionId)) Fail("Question repeated.");
    var questionInstanceId = game.GetProperty("photoAnswerResults").GetProperty("questionInstanceId").GetGuid();
    Console.WriteLine($"Question {questionNumber}: collecting anonymous photos");
    await Observe();

    var submissions = new List<(Guid ClientId, Guid AnswerId)>();
    if (externalHost) await WaitForPhotoProgress("submittedPlayers", 1, TimeSpan.FromSeconds(180));
    for (var index = externalHost ? 1 : 0; index < players.Count; index++)
    {
        var beforeLast = await GetRoom();
        var collectingJson = beforeLast.GetRawText();
        if (collectingJson.Contains("displayPhotoUrl", StringComparison.OrdinalIgnoreCase)) Fail($"Photo URL leaked while collecting: {collectingJson}");
        var clientId = Guid.NewGuid();
        var response = await UploadAnswer(players[index], questionInstanceId, clientId, await CreateImage(index == 0 ? Color.Red : index == 1 ? Color.Green : Color.Blue));
        var answerId = response.GetProperty("photoAnswerId").GetGuid();
        var privateState = response.GetProperty("playerPrivateGameState");
        if (!privateState.GetProperty("hasSubmittedPhotoAnswer").GetBoolean() || privateState.GetProperty("ownPhotoAnswerId").GetGuid() != answerId) Fail("Private photo state is invalid.");
        submissions.Add((clientId, answerId));
    }

    var retry = await UploadAnswer(players[^1], questionInstanceId, submissions[^1].ClientId, await CreateImage(Color.Blue));
    if (retry.GetProperty("photoAnswerId").GetGuid() != submissions[^1].AnswerId) Fail("Idempotent retry created a different answer.");

    var reveal = await WaitForStage("RevealingPhotoAnswers", TimeSpan.FromSeconds(10));
    var revealText = reveal.GetRawText();
    if (revealText.Contains("authorPlayerId", StringComparison.OrdinalIgnoreCase) || revealText.Contains("storageKey", StringComparison.OrdinalIgnoreCase)) Fail("Identity or storage key leaked during reveal.");
    var revealOptions = reveal.GetProperty("game").GetProperty("photoAnswerResults").GetProperty("anonymousOptions").EnumerateArray().ToList();
    if (revealOptions.Count != 3) Fail("Reveal did not contain three photos.");
    foreach (var option in revealOptions)
    {
        var media = await http.GetAsync(option.GetProperty("thumbnailPhotoUrl").GetString());
        if (!media.IsSuccessStatusCode || media.Content.Headers.ContentType?.MediaType != "image/jpeg") Fail("Media URL is unavailable.");
    }
    Console.WriteLine("Anonymous reveal and media URLs verified.");
    await Observe();

    var voting = await WaitForStage("CollectingPhotoAnswerVotes", TimeSpan.FromSeconds(10));
    var votingOptions = voting.GetProperty("game").GetProperty("photoAnswerResults").GetProperty("anonymousOptions").EnumerateArray().ToList();
    await Observe();
    var selected = votingOptions[0].GetProperty("photoAnswerId").GetGuid();
    if (externalHost) await WaitForPhotoProgress("votedPlayers", 1, TimeSpan.FromSeconds(180));
    for (var index = externalHost ? 1 : 0; index < players.Count; index++)
        await playerConnections[index].InvokeAsync("SubmitPhotoAnswerVote", roomCode, players[index].Id, players[index].Token, questionInstanceId, selected);

    var results = await WaitForStage("ShowingPhotoAnswerResults", TimeSpan.FromSeconds(10));
    var resultOptions = results.GetProperty("game").GetProperty("photoAnswerResults").GetProperty("options").EnumerateArray().ToList();
    if (resultOptions.Any(option => !option.TryGetProperty("authorPlayerId", out _))) Fail("Authors were not revealed in results.");
    var winning = resultOptions.Single(option => option.GetProperty("photoAnswerId").GetGuid() == selected);
    if (!externalHost)
    {
        if (winning.GetProperty("voteCount").GetInt32() != 3) Fail("Vote count is invalid.");
        if (winning.GetProperty("voters").EnumerateArray().Any(v => v.GetProperty("pointsAwarded").GetInt32() != 300)) Fail($"Voter scoring is invalid: {winning.GetRawText()}");
    }
    else if (resultOptions.Sum(option => option.GetProperty("voteCount").GetInt32()) != 3 ||
             resultOptions.Sum(option => option.GetProperty("voters").GetArrayLength()) != 3)
    {
        Fail("External iOS vote was not included in backend results.");
    }
    Console.WriteLine("Voting (including own-photo vote), authors and voter points verified.");
    await Observe();
}

await WaitForStage("Completed", TimeSpan.FromSeconds(30));
Console.WriteLine("SUCCESS: photo game reached Completed with four unique questions.");
foreach (var connection in playerConnections) await connection.DisposeAsync();

async Task Observe(int? overrideDelay = null)
{
    var milliseconds = overrideDelay ?? observationDelay;
    if (milliseconds > 0) await Task.Delay(milliseconds);
}

async Task WaitForPhotoProgress(string property, int expected, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (DateTime.UtcNow < deadline)
    {
        var room = await GetRoom();
        if (room.TryGetProperty("game", out var game) && game.ValueKind != JsonValueKind.Null &&
            game.TryGetProperty("photoAnswerResults", out var results) && results.ValueKind != JsonValueKind.Null &&
            results.TryGetProperty(property, out var value) && value.GetInt32() >= expected) return;
        await Task.Delay(100);
    }
    Fail($"Timed out waiting for {property} >= {expected}.");
}

async Task<JsonElement> PostJson(string path, object body)
{
    var response = await http.PostAsJsonAsync(path, body, jsonOptions);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) Fail($"HTTP {(int)response.StatusCode}: {text}");
    return JsonDocument.Parse(text).RootElement.Clone();
}

async Task UploadProfile(PlayerAccess player, byte[] bytes)
{
    using var form = new MultipartFormDataContent();
    var content = new ByteArrayContent(bytes); content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
    form.Add(content, "file", "profile.jpg");
    using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{player.Id}/profile-photo") { Content = form };
    request.Headers.Add("X-Player-Token", player.Token);
    var response = await http.SendAsync(request);
    if (!response.IsSuccessStatusCode) Fail("Profile photo upload failed.");
}

async Task<JsonElement> UploadAnswer(PlayerAccess player, Guid questionId, Guid clientId, byte[] bytes)
{
    using var form = new MultipartFormDataContent();
    form.Add(new StringContent(player.Id.ToString()), "playerId");
    form.Add(new StringContent(player.Token), "reconnectToken");
    form.Add(new StringContent(clientId.ToString()), "clientSubmissionId");
    var photo = new ByteArrayContent(bytes); photo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
    form.Add(photo, "photo", "ignored-client-name.jpg");
    var response = await http.PostAsync($"/api/rooms/{roomCode}/questions/{questionId}/photo-answers", form);
    var text = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode) Fail($"Photo upload failed: {text}");
    return JsonDocument.Parse(text).RootElement.Clone();
}

async Task<JsonElement> GetRoom()
{
    var response = await http.GetAsync($"/api/rooms/{roomCode}");
    response.EnsureSuccessStatusCode();
    return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
}

async Task<JsonElement> WaitForStage(string expected, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    string? lastSeen = null;
    while (DateTime.UtcNow < deadline)
    {
        var room = await GetRoom();
        if (room.TryGetProperty("game", out var game) && game.ValueKind != JsonValueKind.Null)
        {
            var stage = game.GetProperty("stage").GetString();
            if (stage != lastSeen) { Console.WriteLine($"Stage: {stage}"); lastSeen = stage; }
            if (stage == expected) return room;
        }
        await Task.Delay(100);
    }
    Fail($"Timed out waiting for {expected}.");
    return default;
}

static PlayerAccess Access(JsonElement element, string name) => new(element.GetProperty("playerId").GetGuid(), element.GetProperty("reconnectToken").GetString()!, name);
static async Task<byte[]> CreateImage(Color color)
{
    await using var stream = new MemoryStream();
    using var image = new Image<Rgba32>(400, 400, color);
    await image.SaveAsJpegAsync(stream);
    return stream.ToArray();
}
static void Fail(string message) => throw new InvalidOperationException(message);
internal sealed record PlayerAccess(Guid Id, string Token, string Name);
