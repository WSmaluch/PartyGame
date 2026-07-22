using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;

var serverUrl = "http://localhost:5002";
using var http = new HttpClient { BaseAddress = new Uri(serverUrl) };
var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
options.Converters.Add(new JsonStringEnumConverter());

var packagesRes = await http.GetAsync("/api/content/packages");
var packagesBody = await packagesRes.Content.ReadAsStringAsync();
var packagesDoc = JsonDocument.Parse(packagesBody);
var packageId = packagesDoc.RootElement.EnumerateArray().First().GetProperty("key").GetString();

var gameMode = Environment.GetEnvironmentVariable("GAME_MODE") ?? "mixed";
var enabledTypes = gameMode == "text-only" ? new[] { "TextAnswer" } 
    : (gameMode == "mixed" ? new[] { "PlayerSelection", "TextAnswer" } : new[] { "PlayerSelection" });

Console.WriteLine("Creating room...");
var createRes = await http.PostAsJsonAsync("/api/rooms", new 
{ 
    Nickname = "Host",
    SelectedPackageKeys = new[] { packageId },
    EnabledQuestionTypes = enabledTypes,
    Settings = new { RoundCount = 1, QuestionsPerRound = 4, FinalRoundEnabled = true, PlayerSelectionSeconds = 20 }
}, options);
var createBody = await createRes.Content.ReadAsStringAsync();
if (!createRes.IsSuccessStatusCode)
{
    Console.WriteLine($"Failed to create room: {createBody}");
    return;
}
var createDto = JsonSerializer.Deserialize<RoomCreatedDto>(createBody, options);
var roomCode = createDto!.RoomCode;
var hostToken = createDto.ReconnectToken;
var hostId = createDto.PlayerId;

Console.WriteLine($"Room created: {roomCode}. Connecting display...");
var displayConnection = new HubConnectionBuilder()
    .WithUrl($"{serverUrl}/hubs/game")
    .Build();

await displayConnection.StartAsync();
await displayConnection.InvokeAsync("AttachDisplay", roomCode, Guid.NewGuid());
Console.WriteLine("Display attached.");

async Task<(Guid id, string token, HubConnection conn)> JoinPlayer(string nickname)
{
    var res = await http.PostAsJsonAsync($"/api/rooms/{roomCode}/players", new { Nickname = nickname }, options);
    var dto = await res.Content.ReadFromJsonAsync<PlayerJoinedDto>(options);
    
    var conn = new HubConnectionBuilder().WithUrl($"{serverUrl}/hubs/game").Build();
    await conn.StartAsync();
    await conn.InvokeAsync("AttachPlayer", roomCode, dto!.PlayerId, dto.ReconnectToken);
    Console.WriteLine($"Player {nickname} joined ({dto.PlayerId})");
    return (dto.PlayerId, dto.ReconnectToken, conn);
}

// Host connection
var hostConn = new HubConnectionBuilder().WithUrl($"{serverUrl}/hubs/game").Build();
await hostConn.StartAsync();
await hostConn.InvokeAsync("AttachPlayer", roomCode, hostId, hostToken);

var p2 = await JoinPlayer("Wojtek");
var p3 = await JoinPlayer("Kasia");

async Task UploadPhoto(Guid pid, string token)
{
    var content = new MultipartFormDataContent();
    var bytes = new byte[100];
    content.Add(new ByteArrayContent(bytes), "File", "photo.jpg");
    var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{pid}/photo");
    req.Headers.Add("X-Player-Token", token);
    req.Content = content;
    await http.SendAsync(req);
    Console.WriteLine($"Player {pid} uploaded photo.");
}

async Task MarkReady(Guid pid, string token)
{
    var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{pid}/ready");
    req.Headers.Add("X-Player-Token", token);
    await http.SendAsync(req);
    Console.WriteLine($"Player {pid} marked ready.");
}

await UploadPhoto(hostId, hostToken);
await MarkReady(hostId, hostToken);
await UploadPhoto(p2.id, p2.token);
await MarkReady(p2.id, p2.token);
await UploadPhoto(p3.id, p3.token);
await MarkReady(p3.id, p3.token);

Console.WriteLine("Waiting for game to start...");

// Play loop
bool isCompleted = false;
string lastStage = "";
int questionsPlayed = 0;
while (!isCompleted)
{
    var stateRes = await http.GetAsync($"/api/rooms/{roomCode}");
    var stateBody = await stateRes.Content.ReadAsStringAsync();
    var doc = JsonDocument.Parse(stateBody);
    
    if (!doc.RootElement.TryGetProperty("session", out var sessionEl) || sessionEl.ValueKind == JsonValueKind.Null)
    {
        await Task.Delay(500);
        continue;
    }
    
    var stage = sessionEl.GetProperty("stage").GetString();
    if (stage != lastStage)
    {
        Console.WriteLine($"[Stage Transition] -> {stage}");
        lastStage = stage;
    }
    
    if (stage == "Completed")
    {
        Console.WriteLine("SUCCESS: Game reached Completed stage!");
        var scores = sessionEl.GetProperty("scores");
        Console.WriteLine("Final Ranking:");
        foreach(var sc in scores.EnumerateArray())
        {
            var rank = sc.GetProperty("rank").GetInt32();
            var pId = sc.GetProperty("playerId").GetGuid();
            var score = sc.GetProperty("score").GetInt32();
            var nick = pId == hostId ? "Host" : (pId == p2.id ? "Wojtek" : "Kasia");
            Console.WriteLine($"{rank}. {nick} - {score} pkt");
        }
        Console.WriteLine($"Total Played Questions: {questionsPlayed}");
        isCompleted = true;
        break;
    }

    if (stage == "CollectingPlayerSelections")
    {
        Console.WriteLine($"Playing Question {questionsPlayed + 1}...");
        
        async Task SendSelection(Guid pid, string token, Guid targetPid)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{pid}/selection");
            req.Headers.Add("X-Player-Token", token);
            req.Content = JsonContent.Create(new { SelectedPlayerId = targetPid }, options: options);
            await http.SendAsync(req);
        }

        // Send selections: Host -> Kasia (p3), Wojtek -> Kasia, Kasia -> Wojtek
        await SendSelection(hostId, hostToken, p3.id);
        await SendSelection(p2.id, p2.token, p3.id);
        
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await SendSelection(p3.id, p3.token, p2.id);
        watch.Stop();
        
        // Assert immediate transition
        var postStateRes = await http.GetAsync($"/api/rooms/{roomCode}");
        var postDoc = JsonDocument.Parse(await postStateRes.Content.ReadAsStringAsync());
        var newStage = postDoc.RootElement.GetProperty("session").GetProperty("stage").GetString();
        Console.WriteLine($"Immediate transition check: took {watch.ElapsedMilliseconds}ms, stage is {newStage}");
        if (newStage == "ShowingQuestionResults") {
            Console.WriteLine("Validation passed: Immediate transition to ShowingQuestionResults.");
        }
        
        questionsPlayed++;
        
        // Wait till next stage or round summary
        while(true) {
            var s = await http.GetAsync($"/api/rooms/{roomCode}");
            var d = JsonDocument.Parse(await s.Content.ReadAsStringAsync());
            var sStage = d.RootElement.GetProperty("session").GetProperty("stage").GetString();
            if (sStage != "ShowingQuestionResults" && sStage != "CollectingPlayerSelections") {
                break;
            }
            await Task.Delay(500);
        }
    }

    if (stage == "CollectingTextAnswers")
    {
        Console.WriteLine($"Playing TextAnswer Question {questionsPlayed + 1}...");
        
        async Task SendTextAnswer(Guid pid, string token, string txt)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{pid}/text-answer");
            req.Headers.Add("X-Player-Token", token);
            req.Content = JsonContent.Create(new { text = txt }, options: options);
            await http.SendAsync(req);
        }

        await SendTextAnswer(hostId, hostToken, "Host says something funny");
        await SendTextAnswer(p2.id, p2.token, "Wojtek text");
        
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await SendTextAnswer(p3.id, p3.token, "Kasia text");
        watch.Stop();
        
        var postStateRes = await http.GetAsync($"/api/rooms/{roomCode}");
        var postDoc = JsonDocument.Parse(await postStateRes.Content.ReadAsStringAsync());
        var newStage = postDoc.RootElement.GetProperty("session").GetProperty("stage").GetString();
        Console.WriteLine($"Immediate transition check: took {watch.ElapsedMilliseconds}ms, stage is {newStage}");

        // Now wait for Voting stage
        List<Guid> answerIds = new();
        while(true) {
            var s = await http.GetAsync($"/api/rooms/{roomCode}");
            var d = JsonDocument.Parse(await s.Content.ReadAsStringAsync());
            var sStage = d.RootElement.GetProperty("session").GetProperty("stage").GetString();
            if (sStage == "CollectingTextAnswerVotes") {
                var textResults = d.RootElement.GetProperty("session").GetProperty("textResults");
                if (textResults.TryGetProperty("votingOptions", out var opts)) {
                    foreach (var opt in opts.EnumerateArray()) {
                        answerIds.Add(opt.GetProperty("answerId").GetGuid());
                    }
                }
                break;
            }
            await Task.Delay(500);
        }

        if (answerIds.Count > 0)
        {
            Console.WriteLine("Voting...");
            async Task SendVote(Guid pid, string token, Guid aId)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, $"/api/rooms/{roomCode}/players/{pid}/text-answer-vote");
                req.Headers.Add("X-Player-Token", token);
                req.Content = JsonContent.Create(new { SelectedAnswerId = aId }, options: options);
                await http.SendAsync(req);
            }
            
            await SendVote(hostId, hostToken, answerIds[0]);
            await SendVote(p2.id, p2.token, answerIds[0]);
            await SendVote(p3.id, p3.token, answerIds[answerIds.Count > 1 ? 1 : 0]);
        }
        
        questionsPlayed++;
        
        // Wait till next stage or round summary
        while(true) {
            var s = await http.GetAsync($"/api/rooms/{roomCode}");
            var d = JsonDocument.Parse(await s.Content.ReadAsStringAsync());
            var sStage = d.RootElement.GetProperty("session").GetProperty("stage").GetString();
            if (sStage != "ShowingTextAnswerResults" && sStage != "CollectingTextAnswerVotes" && sStage != "RevealingTextAnswers") {
                break;
            }
            await Task.Delay(500);
        }
    }
    await Task.Delay(200);
}

await hostConn.StopAsync();
await displayConnection.StopAsync();
await p2.conn.StopAsync();
await p3.conn.StopAsync();

public record RoomCreatedDto(string RoomCode, Guid PlayerId, string ReconnectToken);
public record PlayerJoinedDto(Guid PlayerId, string ReconnectToken);
