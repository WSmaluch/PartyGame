using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

var backendUrl = Required("PARTYGAME_MIXED_E2E_BACKEND_URL").TrimEnd('/');
var coordinationDir = Required("PARTYGAME_E2E_COORDINATION_DIR");
Directory.CreateDirectory(coordinationDir);

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
using var http = new HttpClient { BaseAddress = new Uri(backendUrl) };
var stage = "package-setup";
var startedEvents = 0;
var questions = new[]
{
    new QuestionDefinition("selection", "PlayerSelection", "Kto wybiera {player}?", "Who chooses {player}?", 0),
    new QuestionDefinition("text", "TextAnswer", "Napisz krótką odpowiedź.", "Write a short answer.", 1),
    new QuestionDefinition("photo", "PhotoAnswer", "Zrób zdjęcie czegoś niebieskiego.", "Take a photo of something blue.", 2),
    new QuestionDefinition("drawing", "DrawingAnswer", "Narysuj prosty symbol.", "Draw a simple symbol.", 3)
};

try
{
    var packageKey = "stage_7_1_mixed";
    var package = await PostJson("/api/admin/content-packages", new
    {
        key = packageKey,
        namePl = "Stage 7.1 Mixed E2E",
        nameEn = "Stage 7.1 Mixed E2E",
        descriptionPl = "Deterministyczny pakiet testowy.",
        descriptionEn = "Deterministic test package."
    });
    var packageId = package.GetProperty("id").GetGuid();

    var category = await PostJson($"/api/admin/content-packages/{packageId}/categories", new
    {
        key = "stage_7_1",
        namePl = "Orkiestracja",
        nameEn = "Orchestration",
        descriptionPl = "Pytania dla Mixed Client E2E.",
        descriptionEn = "Questions for Mixed Client E2E.",
        isActive = true,
        sortOrder = 0,
        packageConcurrencyToken = package.GetProperty("concurrencyToken").GetString()
    });
    var categoryId = category.GetProperty("category").GetProperty("id").GetGuid();

    foreach (var question in questions)
    {
        await PostJson($"/api/admin/content-packages/{packageId}/questions", new
        {
            categoryId,
            key = question.Key,
            type = question.Type,
            textPl = question.TextPl,
            textEn = question.TextEn,
            isActive = true,
            minimumPlayers = 3,
            sortOrder = question.SortOrder
        });
    }

    var categories = await GetJson($"/api/admin/content-packages/{packageId}/categories");
    var published = await PostJson($"/api/admin/content-packages/{packageId}/publish", new
    {
        concurrencyToken = categories.GetProperty("packageConcurrencyToken").GetString()
    });
    if (published.GetProperty("status").GetString() != "Published")
        throw new InvalidOperationException("Deterministyczny package nie został opublikowany.");

    stage = "room-creation";
    var roomAccess = await PostJson("/api/rooms", new
    {
        nickname = "E2E Host",
        contentPackageVersionId = packageId,
        enabledQuestionTypes = new[] { "PlayerSelection", "TextAnswer", "PhotoAnswer", "DrawingAnswer" },
        settings = new
        {
            roundCount = 1,
            questionsPerRound = 4,
            playerSelectionSeconds = 30,
            textAnswerSeconds = 30,
            votingSeconds = 20,
            photoSeconds = 30,
            drawingSeconds = 30,
            resultPresentationSeconds = 5,
            finalRoundEnabled = false,
            finalDrawingPasses = 1
        }
    });
    var roomCode = roomAccess.GetProperty("roomCode").GetString()!;
    var host = Access(roomAccess, "E2E Host");
    var nodeAccess = await PostJson($"/api/rooms/{roomCode}/players", new { nickname = "E2E Node" });
    var node = Access(nodeAccess, "E2E Node");

    var initialRoom = roomAccess.GetProperty("snapshot");
    if (initialRoom.GetProperty("contentPackageVersionId").GetGuid() != packageId)
        throw new InvalidOperationException("Pokój nie został związany z oczekiwaną wersją package.");

    await UploadProfile(roomCode, host, await ProfileImage(Color.Blue));
    await UploadProfile(roomCode, node, await ProfileImage(Color.Green));

    await using var hostConnection = Connection();
    await using var nodeConnection = Connection();
    hostConnection.On<JsonElement>("RoomStarted", _ => Interlocked.Increment(ref startedEvents));
    await hostConnection.StartAsync();
    await nodeConnection.StartAsync();
    await hostConnection.InvokeAsync("AttachPlayer", roomCode, host.Id, host.Token);
    await nodeConnection.InvokeAsync("AttachPlayer", roomCode, node.Id, node.Token);

    await WriteJson("coordination.json", new
    {
        backendUrl,
        roomCode,
        contentPackageVersionId = packageId,
        iosNickname = "E2E iPhone",
        displayExpected = true,
        scriptedPlayers = new[] { host.Name, node.Name }
    });
    Mark("orchestrator-ready");

    stage = "waiting-for-real-clients";
    await WaitForMarker("display-attached", TimeSpan.FromSeconds(240));
    await WaitForMarker("ios-ready", TimeSpan.FromSeconds(90));

    stage = "scripted-ready";
    await hostConnection.InvokeAsync("SetReady", roomCode, host.Id, host.Token, true);
    await nodeConnection.InvokeAsync("SetReady", roomCode, node.Id, node.Token, true);

    stage = "game-start";
    await WaitUntil(() => Volatile.Read(ref startedEvents) == 1, TimeSpan.FromSeconds(30), "RoomStarted exactly once");
    await Task.Delay(1000);
    if (Volatile.Read(ref startedEvents) != 1)
        throw new InvalidOperationException($"RoomStarted wystąpił {Volatile.Read(ref startedEvents)} razy.");

    var startedRoom = await GetJson($"/api/rooms/{roomCode}");
    if (startedRoom.GetProperty("phase").GetString() != "Started")
        throw new InvalidOperationException("Pokój nie przeszedł z Lobby do Started.");
    if (startedRoom.GetProperty("contentPackageVersionId").GetGuid() != packageId)
        throw new InvalidOperationException("Po starcie pokój wskazuje inną wersję package.");
    if (startedRoom.GetProperty("startedAtUtc").ValueKind == JsonValueKind.Null)
        throw new InvalidOperationException("Brakuje startedAtUtc po starcie gry.");
    if (startedRoom.GetProperty("players").EnumerateArray().Any(player => !player.GetProperty("isReady").GetBoolean()))
        throw new InvalidOperationException("Gra wystartowała przed Ready wszystkich graczy.");

    await WriteJson("outcome.json", new
    {
        status = "passed",
        stage,
        roomCode,
        contentPackageVersionId = packageId,
        roomPhase = startedRoom.GetProperty("phase").GetString(),
        stateVersion = startedRoom.GetProperty("stateVersion").GetInt64(),
        roomStartedEvents = Volatile.Read(ref startedEvents),
        ios = "ready",
        display = "attached",
        scriptedPlayers = "ready"
    });
    Mark("game-started");
    Console.WriteLine($"PASS: room {roomCode}, package {packageId}, RoomStarted=1");
}
catch (Exception exception)
{
    await WriteJson("outcome.json", new
    {
        status = "failed",
        stage,
        roomStartedEvents = Volatile.Read(ref startedEvents),
        error = exception.Message
    });
    Console.Error.WriteLine($"FAIL ({stage}): {exception}");
    Environment.ExitCode = 1;
}

HubConnection Connection() => new HubConnectionBuilder().WithUrl($"{backendUrl}/hubs/game").Build();

async Task<JsonElement> PostJson(string path, object body)
{
    using var response = await http.PostAsJsonAsync(path, body, json);
    return await ReadSuccess(response);
}

async Task<JsonElement> GetJson(string path)
{
    using var response = await http.GetAsync(path);
    return await ReadSuccess(response);
}

static async Task<JsonElement> ReadSuccess(HttpResponseMessage response)
{
    var content = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
        throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {content}");
    return JsonDocument.Parse(content).RootElement.Clone();
}

async Task UploadProfile(string roomCode, PlayerAccess player, byte[] image)
{
    using var form = new MultipartFormDataContent();
    var content = new ByteArrayContent(image);
    content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg");
    form.Add(content, "file", "profile.jpg");
    using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{player.Id}/profile-photo") { Content = form };
    request.Headers.Add("X-Player-Token", player.Token);
    using var response = await http.SendAsync(request);
    _ = await ReadSuccess(response);
}

static async Task<byte[]> ProfileImage(Color color)
{
    using var image = new Image<Rgba32>(400, 400, color);
    await using var stream = new MemoryStream();
    await image.SaveAsync(stream, new JpegEncoder());
    return stream.ToArray();
}

async Task WaitForMarker(string name, TimeSpan timeout) =>
    await WaitUntil(() => File.Exists(Path.Combine(coordinationDir, name)), timeout, name);

static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout, string description)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (predicate()) return;
        await Task.Delay(100);
    }
    throw new TimeoutException($"Timeout: {description}");
}

async Task WriteJson(string fileName, object value)
{
    var path = Path.Combine(coordinationDir, fileName);
    var temporaryPath = path + ".tmp";
    await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(value, json));
    File.Move(temporaryPath, path, true);
}

void Mark(string name) => File.WriteAllText(Path.Combine(coordinationDir, name), string.Empty);

static string Required(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"Brak wymaganej zmiennej środowiskowej {name}.");

static PlayerAccess Access(JsonElement response, string name) => new(
    response.GetProperty("playerId").GetGuid(),
    response.GetProperty("reconnectToken").GetString()!,
    name);

internal sealed record PlayerAccess(Guid Id, string Token, string Name);
internal sealed record QuestionDefinition(string Key, string Type, string TextPl, string TextEn, int SortOrder);
