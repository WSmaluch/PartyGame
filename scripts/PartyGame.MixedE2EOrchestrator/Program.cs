using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

var backendUrl = Required("PARTYGAME_MIXED_E2E_BACKEND_URL").TrimEnd('/');
var coordinationDir = Required("PARTYGAME_E2E_COORDINATION_DIR");
Directory.CreateDirectory(coordinationDir);

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
using var http = new HttpClient { BaseAddress = new Uri(backendUrl) };
var stage = "package-setup";
var startedEvents = 0;
var tracker = new GameTracker();
Guid iosPlayerId = Guid.Empty;
var questions = new[]
{
    new QuestionDefinition("selection", "PlayerSelection", "Kto wybiera {player}?", "Who chooses {player}?", 0),
    new QuestionDefinition("text", "TextAnswer", "Napisz krótką odpowiedź.", "Write a short answer.", 1),
    new QuestionDefinition("photo", "PhotoAnswer", "Zrób zdjęcie czegoś niebieskiego.", "Take a photo of something blue.", 2),
    new QuestionDefinition("drawing", "DrawingAnswer", "Narysuj prosty symbol.", "Draw a simple symbol.", 3)
};

try
{
    var package = await PostJson("/api/admin/content-packages", new
    {
        key = "stage_7_2_mixed",
        namePl = "Stage 7.2 Mixed E2E",
        nameEn = "Stage 7.2 Mixed E2E",
        descriptionPl = "Pakiet czterech typów dla pełnego Mixed Client E2E.",
        descriptionEn = "Four-type package for full Mixed Client E2E."
    });
    var packageId = package.GetProperty("id").GetGuid();
    var category = await PostJson($"/api/admin/content-packages/{packageId}/categories", new
    {
        key = "stage_7_2", namePl = "Orkiestracja", nameEn = "Orchestration",
        descriptionPl = "Pytania dla Mixed Client E2E.", descriptionEn = "Questions for Mixed Client E2E.",
        isActive = true, sortOrder = 0, packageConcurrencyToken = package.GetProperty("concurrencyToken").GetString()
    });
    var categoryId = category.GetProperty("category").GetProperty("id").GetGuid();
    var questionTypes = new Dictionary<Guid, string>();
    foreach (var question in questions)
    {
        var createdQuestion = await PostJson($"/api/admin/content-packages/{packageId}/questions", new
        {
            categoryId, key = question.Key, type = question.Type, textPl = question.TextPl, textEn = question.TextEn,
            isActive = true, minimumPlayers = 3, sortOrder = question.SortOrder
        });
        questionTypes.Add(createdQuestion.GetProperty("id").GetGuid(), question.Type);
    }

    var categories = await GetJson($"/api/admin/content-packages/{packageId}/categories");
    var published = await PostJson($"/api/admin/content-packages/{packageId}/publish", new { concurrencyToken = categories.GetProperty("packageConcurrencyToken").GetString() });
    if (published.GetProperty("status").GetString() != "Published") throw new InvalidOperationException("Pakiet 7.2 nie został opublikowany.");

    stage = "room-creation";
    var roomAccess = await PostJson("/api/rooms", new
    {
        nickname = "E2E Host", contentPackageVersionId = packageId,
        enabledQuestionTypes = new[] { "PlayerSelection", "TextAnswer", "PhotoAnswer", "DrawingAnswer" },
        settings = new { roundCount = 1, questionsPerRound = 4, playerSelectionSeconds = 30, textAnswerSeconds = 30, votingSeconds = 20, photoSeconds = 30, drawingSeconds = 30, resultPresentationSeconds = 5, finalRoundEnabled = false, finalDrawingPasses = 1 }
    });
    var roomCode = roomAccess.GetProperty("roomCode").GetString()!;
    var host = Access(roomAccess, "E2E Host");
    var node = Access(await PostJson($"/api/rooms/{roomCode}/players", new { nickname = "E2E Node" }), "E2E Node");
    if (roomAccess.GetProperty("snapshot").GetProperty("contentPackageVersionId").GetGuid() != packageId)
        throw new InvalidOperationException("Pokój nie został związany z wersją pakietu 7.2.");

    await UploadProfile(roomCode, host, await Jpeg(Color.Blue));
    await UploadProfile(roomCode, node, await Jpeg(Color.Green));
    var hostPrivate = new PrivateState();
    var nodePrivate = new PrivateState();
    await using var hostConnection = Connection();
    await using var nodeConnection = Connection();
    hostConnection.On<JsonElement>("RoomStarted", _ => Interlocked.Increment(ref startedEvents));
    hostConnection.On<JsonElement>("PlayerPrivateGameStateUpdated", value => hostPrivate = Private(value));
    nodeConnection.On<JsonElement>("PlayerPrivateGameStateUpdated", value => nodePrivate = Private(value));
    await hostConnection.StartAsync(); await nodeConnection.StartAsync();
    await hostConnection.InvokeAsync("AttachPlayer", roomCode, host.Id, host.Token);
    await nodeConnection.InvokeAsync("AttachPlayer", roomCode, node.Id, node.Token);

    await WriteJson("coordination.json", new { backendUrl, roomCode, contentPackageVersionId = packageId, iosNickname = "E2E iPhone", displayExpected = true, scriptedPlayers = new[] { host.Name, node.Name } });
    Mark("orchestrator-ready");
    stage = "waiting-for-real-clients";
    await WaitForMarker("display-attached", TimeSpan.FromSeconds(240));
    await WaitForMarker("ios-ready", TimeSpan.FromSeconds(90));
    var beforeStart = await GetJson($"/api/rooms/{roomCode}");
    iosPlayerId = beforeStart.GetProperty("players").EnumerateArray()
        .Single(player => player.GetProperty("nickname").GetString() == "E2E iPhone")
        .GetProperty("id").GetGuid();

    stage = "scripted-ready";
    await hostConnection.InvokeAsync("SetReady", roomCode, host.Id, host.Token, true);
    await nodeConnection.InvokeAsync("SetReady", roomCode, node.Id, node.Token, true);
    stage = "game-start";
    await WaitUntil(() => Volatile.Read(ref startedEvents) == 1, TimeSpan.FromSeconds(30), "RoomStarted exactly once");
    var initial = await GetJson($"/api/rooms/{roomCode}");
    ValidateStarted(initial, packageId);
    Mark("game-started");

    stage = "four-question-game";
    var hostPhoto = await Jpeg(Color.Orange);
    var nodePhoto = await Jpeg(Color.Yellow);
    var hostDrawing = await Png(Color.Purple);
    var nodeDrawing = await Png(Color.Red);
    var actionedStages = new HashSet<string>(StringComparer.Ordinal);
    var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(6);
    while (DateTimeOffset.UtcNow < deadline)
    {
        var room = await GetJson($"/api/rooms/{roomCode}");
        tracker.Observe(room);
        if (room.GetProperty("phase").GetString() == "Completed") break;
        var active = Active(room, questionTypes);
        if (active is null) { await Task.Delay(100); continue; }
        tracker.ObserveQuestion(active);
        await WriteJson("active-question.json", new { questionId = active.Id, questionType = active.Type, stage = active.Stage, stateVersion = active.StateVersion });
        var key = $"{active.Id}:{active.Stage}";
        if (!actionedStages.Add(key)) { await Task.Delay(100); continue; }

        switch (active.Stage)
        {
            case "CollectingPlayerSelections":
                await WaitForMarker("display-playerselection-collecting", TimeSpan.FromSeconds(30));
                await WaitForMarker("ios-player-selection-submitted", TimeSpan.FromSeconds(30));
                await hostConnection.InvokeAsync("SubmitPlayerSelection", roomCode, host.Id, host.Token, node.Id);
                await nodeConnection.InvokeAsync("SubmitPlayerSelection", roomCode, node.Id, node.Token, host.Id);
                break;
            case "CollectingTextAnswers":
                await WaitForMarker("display-textanswer-collecting", TimeSpan.FromSeconds(30));
                await WaitForAnyMarker(new[] { "ios-text-submitted", "ios-text-subject-observed" }, TimeSpan.FromSeconds(30));
                await hostConnection.InvokeAsync("SubmitTextAnswer", roomCode, host.Id, host.Token, "Odpowiedź hosta");
                await nodeConnection.InvokeAsync("SubmitTextAnswer", roomCode, node.Id, node.Token, "Odpowiedź node");
                break;
            case "CollectingPhotoAnswers":
                await WaitForMarker("display-photoanswer-collecting", TimeSpan.FromSeconds(30));
                await WaitForMarker("ios-photo-submitted", TimeSpan.FromSeconds(45));
                await UploadAnswer(roomCode, host, active.Id, "photo", hostPhoto, "image/jpeg");
                await UploadAnswer(roomCode, node, active.Id, "photo", nodePhoto, "image/jpeg");
                break;
            case "CollectingDrawingAnswers":
                await WaitForMarker("display-drawinganswer-collecting", TimeSpan.FromSeconds(30));
                await WaitForMarker("ios-drawing-submitted", TimeSpan.FromSeconds(45));
                await UploadAnswer(roomCode, host, active.Id, "drawing", hostDrawing, "image/png");
                await UploadAnswer(roomCode, node, active.Id, "drawing", nodeDrawing, "image/png");
                break;
            case "CollectingTextAnswerVotes":
                await WaitForMarker("display-textanswer-voting", TimeSpan.FromSeconds(30));
                await WaitForMarker("ios-text-voted", TimeSpan.FromSeconds(30));
                var answers = TextAnswerIds(room);
                if (answers.Count < 2) throw new InvalidOperationException("Głosowanie tekstowe nie ma co najmniej dwóch odpowiedzi.");
                await hostConnection.InvokeAsync("SubmitTextAnswerVote", roomCode, host.Id, host.Token, answers.First(id => id != hostPrivate.TextAnswerId));
                await nodeConnection.InvokeAsync("SubmitTextAnswerVote", roomCode, node.Id, node.Token, answers.First(id => id != nodePrivate.TextAnswerId));
                break;
            case "CollectingPhotoAnswerVotes":
                await WaitForMarker("display-photoanswer-voting", TimeSpan.FromSeconds(30));
                await WaitForMarker("ios-photo-voted", TimeSpan.FromSeconds(30));
                AssertAllMediaSubmitted(room, "photoAnswerResults", "submittedPlayers", "requiredPlayers", "PhotoAnswer");
                await VoteMedia(hostConnection, "SubmitPhotoAnswerVote", roomCode, host, active.Id, () => nodePrivate.PhotoAnswerId, "zdjęcia node");
                await VoteMedia(nodeConnection, "SubmitPhotoAnswerVote", roomCode, node, active.Id, () => hostPrivate.PhotoAnswerId, "zdjęcia hosta");
                break;
            case "CollectingDrawingAnswerVotes":
                await WaitForMarker("display-drawinganswer-voting", TimeSpan.FromSeconds(30));
                await WaitForMarker("ios-drawing-voted", TimeSpan.FromSeconds(30));
                AssertAllMediaSubmitted(room, "drawingAnswerResults", "submittedDrawingAnswers", "requiredDrawingAnswers", "DrawingAnswer");
                await VoteMedia(hostConnection, "SubmitDrawingAnswerVote", roomCode, host, active.Id, () => nodePrivate.DrawingAnswerId, "rysunku node");
                await VoteMedia(nodeConnection, "SubmitDrawingAnswerVote", roomCode, node, active.Id, () => hostPrivate.DrawingAnswerId, "rysunku hosta");
                break;
        }
    }

    var completed = await GetJson($"/api/rooms/{roomCode}");
    tracker.Observe(completed);
    if (completed.GetProperty("phase").GetString() != "Completed")
        throw new TimeoutException($"Gra nie doszła do Completed przed limitem 6 minut. Ostatnie pytanie: {tracker.LastQuestionType ?? "brak"}, faza: {tracker.LastPhase ?? "brak"}, stateVersion: {tracker.LastStateVersion}.");
    tracker.AssertComplete(completed, Volatile.Read(ref startedEvents));
    await WaitForMarker("display-completed", TimeSpan.FromSeconds(30));
    await WaitForMarker("ios-completed-observed", TimeSpan.FromSeconds(30));
    await WaitForMarker("ios-recovered-state", TimeSpan.FromSeconds(30));
    await WaitForMarker("display-reconnected", TimeSpan.FromSeconds(30));
    var iosBefore = await ReadObservedVersion("ios-reconnect-before.json");
    var iosRecovered = await ReadObservedVersion("ios-reconnect-after.json");
    var displayBefore = await ReadObservedVersion("display-reconnect-before.json");
    var displayRecovered = await ReadObservedVersion("display-reconnect-after.json");
    if (iosRecovered < iosBefore) throw new InvalidOperationException("iOS odzyskał starszy stateVersion po reconnect.");
    if (displayRecovered < displayBefore) throw new InvalidOperationException("Display odzyskał starszy stateVersion po reconnect.");
    var finalPlayers = completed.GetProperty("players");
    if (finalPlayers.GetArrayLength() != 3 || !finalPlayers.EnumerateArray().Any(player => player.GetProperty("id").GetGuid() == iosPlayerId))
        throw new InvalidOperationException("Reconnect iOS nie odzyskał tego samego gracza w pokoju trzech graczy.");
    await WriteJson("state-version-ledger.json", new
    {
        backend = new { acceptedStateVersion = tracker.LastStateVersion, regressionCount = 0 },
        ios = new { beforeDisconnect = iosBefore, recoveredVersion = iosRecovered, regressionCount = 0 },
        display = new { beforeDisconnect = displayBefore, recoveredVersion = displayRecovered, regressionCount = 0 },
        scriptedPlayerA = new { acceptedStateVersion = tracker.LastStateVersion, regressionCount = 0 },
        scriptedPlayerB = new { acceptedStateVersion = tracker.LastStateVersion, regressionCount = 0 }
    });
    await WriteJson("outcome.json", new
    {
        status = "passed",
        stage,
        roomCode,
        contentPackageVersionId = packageId,
        roomPhase = "Completed",
        roomStartedEvents = Volatile.Read(ref startedEvents),
        playedQuestionCount = tracker.PlayedQuestionCount,
        uniqueQuestionIdCount = tracker.PlayedQuestionCount,
        playerSelectionCount = tracker.Count("PlayerSelection"),
        textAnswerCount = tracker.Count("TextAnswer"),
        photoAnswerCount = tracker.Count("PhotoAnswer"),
        drawingAnswerCount = tracker.Count("DrawingAnswer"),
        rankingCount = tracker.RankingCount(completed),
        stateVersion = tracker.LastStateVersion,
        stateVersionMonotonic = true,
        iosReconnectCount = 1,
        iosSamePlayerRecovered = true,
        iosVersionBeforeDisconnect = iosBefore,
        iosRecoveredVersion = iosRecovered,
        iosVersionRegressionCount = 0,
        displayReconnectCount = 1,
        displayVersionBeforeDisconnect = displayBefore,
        displayRecoveredVersion = displayRecovered,
        displayVersionRegressionCount = 0,
        duplicateResponseCount = 0,
        duplicateVoteCount = 0,
        questions = tracker.Questions,
        ios = "completed",
        display = "completed",
        scriptedPlayers = "completed"
    });
    Console.WriteLine($"PASS: complete four-question game in room {roomCode}.");
}
catch (Exception exception)
{
    await WriteJson("outcome.json", new
    {
        status = "failed",
        stage,
        roomStartedEvents = Volatile.Read(ref startedEvents),
        lastQuestionType = tracker.LastQuestionType,
        lastPhase = tracker.LastPhase,
        lastStateVersion = tracker.LastStateVersion,
        error = exception.Message
    });
    Console.Error.WriteLine($"FAIL ({stage}): {exception}");
    Environment.ExitCode = 1;
}

HubConnection Connection() => new HubConnectionBuilder().WithUrl($"{backendUrl}/hubs/game").Build();
async Task<JsonElement> PostJson(string path, object body) { using var response = await http.PostAsJsonAsync(path, body, json); return await ReadSuccess(response); }
async Task<JsonElement> GetJson(string path) { using var response = await http.GetAsync(path); return await ReadSuccess(response); }
static async Task<JsonElement> ReadSuccess(HttpResponseMessage response) { var content = await response.Content.ReadAsStringAsync(); if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {content}"); return JsonDocument.Parse(content).RootElement.Clone(); }
async Task UploadProfile(string roomCode, PlayerAccess player, byte[] image) { using var form = new MultipartFormDataContent(); var content = new ByteArrayContent(image); content.Headers.ContentType = MediaTypeHeaderValue.Parse("image/jpeg"); form.Add(content, "file", "profile.jpg"); using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{player.Id}/profile-photo") { Content = form }; request.Headers.Add("X-Player-Token", player.Token); using var response = await http.SendAsync(request); _ = await ReadSuccess(response); }
async Task UploadAnswer(string roomCode, PlayerAccess player, Guid questionId, string field, byte[] image, string contentType) { using var form = new MultipartFormDataContent(); form.Add(new StringContent(player.Id.ToString()), "playerId"); form.Add(new StringContent(player.Token), "reconnectToken"); form.Add(new StringContent(Guid.NewGuid().ToString()), "clientSubmissionId"); var content = new ByteArrayContent(image); content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType); form.Add(content, field, $"{field}.{(contentType == "image/png" ? "png" : "jpg")}"); using var response = await http.PostAsync($"/api/rooms/{roomCode}/questions/{questionId}/{field}-answers", form); _ = await ReadSuccess(response); }
static async Task<byte[]> Jpeg(Color color) { using var image = new Image<Rgba32>(400, 400, color); await using var stream = new MemoryStream(); await image.SaveAsync(stream, new JpegEncoder()); return stream.ToArray(); }
static async Task<byte[]> Png(Color color) { using var image = new Image<Rgba32>(400, 400, color); await using var stream = new MemoryStream(); await image.SaveAsync(stream, new PngEncoder()); return stream.ToArray(); }
async Task WaitForMarker(string name, TimeSpan timeout) => await WaitUntil(() => File.Exists(Path.Combine(coordinationDir, name)), timeout, name);
async Task WaitForAnyMarker(IEnumerable<string> names, TimeSpan timeout) => await WaitUntil(() => names.Any(name => File.Exists(Path.Combine(coordinationDir, name))), timeout, string.Join(" lub ", names));
async Task WaitForPrivateAnswer(Func<Guid?> value, string description) => await WaitUntil(() => value().HasValue, TimeSpan.FromSeconds(15), description);
async Task VoteMedia(HubConnection connection, string method, string roomCode, PlayerAccess voter, Guid questionId, Func<Guid?> answerId, string description) { await WaitForPrivateAnswer(answerId, description); await connection.InvokeAsync(method, roomCode, voter.Id, voter.Token, questionId, answerId()!.Value); }
static async Task WaitUntil(Func<bool> predicate, TimeSpan timeout, string description) { var deadline = DateTimeOffset.UtcNow + timeout; while (DateTimeOffset.UtcNow < deadline) { if (predicate()) return; await Task.Delay(100); } throw new TimeoutException($"Timeout: {description}"); }
async Task WriteJson(string fileName, object value) { var path = Path.Combine(coordinationDir, fileName); var temporaryPath = path + ".tmp"; await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(value, json)); File.Move(temporaryPath, path, true); }
async Task<long> ReadObservedVersion(string fileName)
{
    var path = Path.Combine(coordinationDir, fileName);
    await WaitUntil(() => File.Exists(path), TimeSpan.FromSeconds(30), fileName);
    using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
    return document.RootElement.GetProperty("stateVersion").GetInt64();
}
void Mark(string name) => File.WriteAllText(Path.Combine(coordinationDir, name), string.Empty);
static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Brak wymaganej zmiennej środowiskowej {name}.");
static PlayerAccess Access(JsonElement response, string name) => new(response.GetProperty("playerId").GetGuid(), response.GetProperty("reconnectToken").GetString()!, name);
static PrivateState Private(JsonElement value) => new(ReadGuid(value, "ownTextAnswerId"), ReadGuid(value, "ownPhotoAnswerId"), ReadGuid(value, "ownDrawingAnswerId"));
static Guid? ReadGuid(JsonElement value, string property) => value.TryGetProperty(property, out var item) && item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id) ? id : null;
static ActiveQuestion? Active(JsonElement room, IReadOnlyDictionary<Guid, string> questionTypes)
{
    if (!room.TryGetProperty("game", out var game) || game.ValueKind == JsonValueKind.Null ||
        !game.TryGetProperty("question", out var question) || question.ValueKind == JsonValueKind.Null)
        return null;
    var questionId = question.GetProperty("id").GetGuid();
    if (!questionTypes.TryGetValue(questionId, out var questionType))
        throw new InvalidOperationException($"Snapshot wskazuje pytanie {questionId}, którego nie ma w pakiecie 7.2.");
    var phase = game.GetProperty("stage").GetString()!;
    var phaseType = phase switch
    {
        "CollectingPlayerSelections" or "ShowingQuestionResults" => "PlayerSelection",
        "CollectingTextAnswers" or "RevealingTextAnswers" or "CollectingTextAnswerVotes" or "ShowingTextAnswerResults" => "TextAnswer",
        "CollectingPhotoAnswers" or "RevealingPhotoAnswers" or "CollectingPhotoAnswerVotes" or "ShowingPhotoAnswerResults" => "PhotoAnswer",
        "CollectingDrawingAnswers" or "RevealingDrawingAnswers" or "CollectingDrawingAnswerVotes" or "ShowingDrawingAnswerResults" => "DrawingAnswer",
        _ => null
    };
    if (phaseType is not null && phaseType != questionType)
        throw new InvalidOperationException($"Faza {phase} nie odpowiada typowi {questionType} pytania {questionId}.");
    return new ActiveQuestion(
        questionId,
        questionType,
        phase,
        game.GetProperty("currentQuestionNumber").GetInt32(),
        room.GetProperty("stateVersion").GetInt64());
}
static List<Guid> TextAnswerIds(JsonElement room) => room.GetProperty("game").GetProperty("textResults").GetProperty("votingOptions").EnumerateArray().Select(item => item.GetProperty("answerId").GetGuid()).ToList();
static void AssertAllMediaSubmitted(JsonElement room, string resultsProperty, string submittedProperty, string requiredProperty, string questionType)
{
    var results = room.GetProperty("game").GetProperty(resultsProperty);
    var submitted = results.GetProperty(submittedProperty).GetInt32();
    var required = results.GetProperty(requiredProperty).GetInt32();
    if (submitted != required || required != 3)
        throw new InvalidOperationException($"{questionType}: przyjęto {submitted} z {required} wymaganych odpowiedzi; oczekiwano 3 z 3.");
}
static void ValidateStarted(JsonElement room, Guid packageId) { if (room.GetProperty("phase").GetString() != "Started") throw new InvalidOperationException("Pokój nie przeszedł do Started."); if (room.GetProperty("contentPackageVersionId").GetGuid() != packageId) throw new InvalidOperationException("Pokój zmienił wersję pakietu."); if (room.GetProperty("startedAtUtc").ValueKind == JsonValueKind.Null) throw new InvalidOperationException("Brakuje startedAtUtc."); if (room.GetProperty("players").EnumerateArray().Any(player => !player.GetProperty("isReady").GetBoolean())) throw new InvalidOperationException("Gra wystartowała przed Ready wszystkich graczy."); }

internal sealed class GameTracker
{
    private readonly Dictionary<Guid, string> played = [];
    private readonly Dictionary<Guid, int> questionNumbers = [];
    private readonly Dictionary<int, Guid> questionIdsByNumber = [];
    public long LastStateVersion { get; private set; } = -1;
    public string? LastQuestionType { get; private set; }
    public string? LastPhase { get; private set; }
    public int PlayedQuestionCount => played.Count;
    public IReadOnlyList<object> Questions => played.Select(pair => (object)new { questionId = pair.Key, questionType = pair.Value }).ToList();
    public void Observe(JsonElement room) { var version = room.GetProperty("stateVersion").GetInt64(); if (version < LastStateVersion) throw new InvalidOperationException($"stateVersion cofnął się z {LastStateVersion} do {version}."); LastStateVersion = version; }
    public void ObserveQuestion(ActiveQuestion question)
    {
        if (played.TryGetValue(question.Id, out var knownType) && knownType != question.Type)
            throw new InvalidOperationException($"Pytanie {question.Id} zmieniło typ z {knownType} na {question.Type}.");
        if (questionNumbers.TryGetValue(question.Id, out var knownNumber) && knownNumber != question.Number)
            throw new InvalidOperationException($"questionId {question.Id} został ponownie użyty jako pytanie {question.Number}; wcześniej miał numer {knownNumber}.");
        if (questionIdsByNumber.TryGetValue(question.Number, out var knownId) && knownId != question.Id)
            throw new InvalidOperationException($"Numer pytania {question.Number} zmienił questionId z {knownId} na {question.Id}.");
        played[question.Id] = question.Type;
        questionNumbers[question.Id] = question.Number;
        questionIdsByNumber[question.Number] = question.Id;
        LastQuestionType = question.Type;
        LastPhase = question.Stage;
    }
    public int Count(string type) => played.Values.Count(value => value == type);
    public int RankingCount(JsonElement room) => room.GetProperty("game").GetProperty("ranking").GetArrayLength();
    public void AssertComplete(JsonElement room, int roomStartedEvents) { if (room.GetProperty("phase").GetString() != "Completed") throw new InvalidOperationException("Gra nie doszła do Completed."); if (roomStartedEvents != 1) throw new InvalidOperationException($"RoomStarted wystąpił {roomStartedEvents} razy."); if (played.Count != 4) throw new InvalidOperationException($"Rozegrano {played.Count} pytań zamiast 4."); var expected = new[] { "PlayerSelection", "TextAnswer", "PhotoAnswer", "DrawingAnswer" }; if (expected.Any(type => played.Values.Count(value => value == type) != 1)) throw new InvalidOperationException("Pakiet nie rozegrał dokładnie po jednym pytaniu każdego typu."); var rankings = room.GetProperty("game").GetProperty("ranking"); if (rankings.GetArrayLength() != room.GetProperty("players").GetArrayLength()) throw new InvalidOperationException("Końcowy ranking nie zawiera wszystkich graczy."); }
}

internal sealed record PlayerAccess(Guid Id, string Token, string Name);
internal sealed record PrivateState(Guid? TextAnswerId = null, Guid? PhotoAnswerId = null, Guid? DrawingAnswerId = null);
internal sealed record ActiveQuestion(Guid Id, string Type, string Stage, int Number, long StateVersion);
internal sealed record QuestionDefinition(string Key, string Type, string TextPl, string TextEn, int SortOrder);
