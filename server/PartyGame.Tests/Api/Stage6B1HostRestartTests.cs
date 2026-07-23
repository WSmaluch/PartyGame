using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class Stage6B1HostRestartTests
{
    [Fact]
    public async Task RealHostRestart_PreservesProfilePhotoPhotoAnswerAndDrawingAnswer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.HostRestart", Guid.NewGuid().ToString("N"));
        var settings = new Dictionary<string, string?> { ["GameFlow:WorkerIntervalMilliseconds"] = "100" };
        List<MediaFingerprint> expected;

        try
        {
            await using (var hostA = new PhotoAnswerTestHarness(directory, deleteOnDispose: false, settings: settings))
            {
                await using var photoRoom = await CreateStartedRoomAsync(hostA, "PhotoAnswer");
                var profile = await ReadProfileFingerprintAsync(hostA, photoRoom.Host);
                var photo = await UploadPhotoAndReadFingerprintAsync(hostA, photoRoom);

                await using var drawingRoom = await CreateStartedRoomAsync(hostA, "DrawingAnswer");
                var drawing = await UploadDrawingAndReadFingerprintAsync(hostA, drawingRoom);
                expected = [profile, photo, drawing];

                Assert.Equal(3, expected.Select(asset => asset.Id).Distinct().Count());
                Assert.All(expected, asset => Assert.True(File.Exists(Path.Combine(hostA.Factory.MediaRootPath, asset.DisplayStorageKey))));
            }

            await using var hostB = new PhotoAnswerTestHarness(directory, deleteOnDispose: false, settings: settings);
            foreach (var asset in expected)
            {
                await AssertPersistedMetadataAsync(hostB, asset);
                var response = await hostB.Client.GetAsync(asset.ProfilePhoto
                    ? $"/api/rooms/{asset.RoomCode}/players/{asset.PlayerId}/profile-photo"
                    : $"/api/media/{asset.Id}/display");
                response.EnsureSuccessStatusCode();
                var bytes = await response.Content.ReadAsByteArrayAsync();

                Assert.Equal(asset.ContentType, response.Content.Headers.ContentType?.MediaType);
                Assert.Equal(asset.ByteLength, response.Content.Headers.ContentLength);
                Assert.Equal(asset.ByteLength, bytes.LongLength);
                Assert.Equal(asset.Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
                Assert.True(asset.ContentType == "image/jpeg"
                    ? bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xd8, 0xff })
                    : bytes.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47 }));
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<StartedRoom> CreateStartedRoomAsync(PhotoAnswerTestHarness harness, string questionType)
    {
        var roomSettings = new RoomSettingsRequest(1, 4, 5, 5, 5, 60, 60, 3, false, 1);
        var host = await CreateAsync(harness, $"{questionType} Host", roomSettings, questionType);
        var second = await JoinAsync(harness, host.RoomCode, $"{questionType} Two");
        var third = await JoinAsync(harness, host.RoomCode, $"{questionType} Three");
        var players = new[] { host, second, third };

        var display = Connection(harness);
        var first = Connection(harness);
        var secondConnection = Connection(harness);
        var thirdConnection = Connection(harness);
        var connections = new[] { first, secondConnection, thirdConnection };
        await Task.WhenAll(connections.Append(display).Select(connection => connection.StartAsync()));
        await display.InvokeAsync<RoomSnapshot>("AttachDisplay", host.RoomCode);
        await Task.WhenAll(players.Zip(connections).Select(pair => pair.Second.InvokeAsync<RoomSnapshot>("AttachPlayer", pair.First.RoomCode, pair.First.PlayerId, pair.First.ReconnectToken)));
        await Task.WhenAll(players.Select(player => DrawingAnswerGameE2ETests.UploadProfile(harness, player)));
        await Task.WhenAll(players.Zip(connections).Select(pair => pair.Second.InvokeAsync<RoomSnapshot>("SetReady", pair.First.RoomCode, pair.First.PlayerId, pair.First.ReconnectToken, true)));

        var expectedStage = questionType == "PhotoAnswer" ? GameStage.CollectingPhotoAnswers : GameStage.CollectingDrawingAnswers;
        var questionInstanceId = await WaitForStageAsync(harness, host.RoomCode, expectedStage);
        return new StartedRoom(host.RoomCode, host, players, questionInstanceId, [display, first, secondConnection, thirdConnection]);
    }

    private static async Task<MediaFingerprint> ReadProfileFingerprintAsync(PhotoAnswerTestHarness harness, RoomAccessResponse player)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var profileAssetId = await db.Players
            .Where(candidate => candidate.Id == player.PlayerId)
            .Select(candidate => candidate.ProfilePhotoMediaAssetId)
            .SingleAsync();
        return await ReadFingerprintAsync(db, profileAssetId!.Value, player.RoomCode, profilePhoto: true);
    }

    private static async Task<MediaFingerprint> UploadPhotoAndReadFingerprintAsync(PhotoAnswerTestHarness harness, StartedRoom room)
    {
        var jpeg = await PhotoAnswerTestHarness.ImageAsync();
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var eligiblePlayerIds = await db.PhotoAnswerEligiblePlayers
            .Where(candidate => candidate.QuestionInstanceId == room.QuestionInstanceId)
            .Select(candidate => candidate.PlayerId)
            .ToListAsync();
        var eligiblePlayers = room.Players.Where(player => eligiblePlayerIds.Contains(player.PlayerId)).ToList();
        Assert.NotEmpty(eligiblePlayers);
        foreach (var player in eligiblePlayers)
            await PostPhotoAsync(harness, player, room.QuestionInstanceId, jpeg);

        var authorPlayerId = eligiblePlayers[0].PlayerId;
        var assetId = await db.PhotoAnswerSubmissions
            .Where(submission => submission.QuestionInstanceId == room.QuestionInstanceId && submission.AuthorPlayerId == authorPlayerId)
            .Select(submission => submission.MediaAssetId)
            .SingleAsync();
        return await ReadFingerprintAsync(db, assetId, room.RoomCode, profilePhoto: false);
    }

    private static async Task<MediaFingerprint> UploadDrawingAndReadFingerprintAsync(PhotoAnswerTestHarness harness, StartedRoom room)
    {
        var png = await PhotoAnswerTestHarness.DrawingAsync();
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var eligiblePlayerIds = await db.DrawingAnswerEligiblePlayers
            .Where(candidate => candidate.QuestionInstanceId == room.QuestionInstanceId)
            .Select(candidate => candidate.PlayerId)
            .ToListAsync();
        var eligiblePlayers = room.Players.Where(player => eligiblePlayerIds.Contains(player.PlayerId)).ToList();
        Assert.NotEmpty(eligiblePlayers);
        foreach (var player in eligiblePlayers)
            await PostDrawingAsync(harness, player, room.QuestionInstanceId, png);

        var authorPlayerId = eligiblePlayers[0].PlayerId;
        var assetId = await db.DrawingAnswerSubmissions
            .Where(submission => submission.QuestionInstanceId == room.QuestionInstanceId && submission.AuthorPlayerId == authorPlayerId)
            .Select(submission => submission.MediaAssetId)
            .SingleAsync();
        return await ReadFingerprintAsync(db, assetId, room.RoomCode, profilePhoto: false);
    }

    private static async Task PostPhotoAsync(PhotoAnswerTestHarness harness, RoomAccessResponse player, Guid questionInstanceId, byte[] jpeg)
    {
        using var form = MultipartForm(player, "photo", "answer.jpg", "image/jpeg", jpeg);
        var response = await harness.Client.PostAsync($"/api/rooms/{player.RoomCode}/questions/{questionInstanceId}/photo-answers", form);
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"PhotoAnswer upload failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task PostDrawingAsync(PhotoAnswerTestHarness harness, RoomAccessResponse player, Guid questionInstanceId, byte[] png)
    {
        using var form = MultipartForm(player, "drawing", "drawing.png", "image/png", png);
        var response = await harness.Client.PostAsync($"/api/rooms/{player.RoomCode}/questions/{questionInstanceId}/drawing-answers", form);
        if (!response.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"DrawingAnswer upload failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    private static MultipartFormDataContent MultipartForm(RoomAccessResponse player, string field, string fileName, string contentType, byte[] bytes)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(player.PlayerId.ToString()), "playerId");
        form.Add(new StringContent(player.ReconnectToken), "reconnectToken");
        form.Add(new StringContent(Guid.NewGuid().ToString()), "clientSubmissionId");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(file, field, fileName);
        return form;
    }

    private static async Task<Guid> WaitForStageAsync(PhotoAnswerTestHarness harness, string roomCode, GameStage expectedStage)
    {
        GameStage? lastStage = null;
        for (var attempt = 0; attempt < 1_600; attempt++)
        {
            await using var scope = harness.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            var session = await db.GameSessions.AsNoTracking().SingleAsync(candidate => candidate.Room.Code == roomCode);
            lastStage = session.Stage;
            if (session.Stage == expectedStage && session.CurrentQuestionInstanceId is Guid instanceId)
                return instanceId;
            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException($"Room {roomCode} did not reach {expectedStage}; last stage was {lastStage}.");
    }

    private static async Task<MediaFingerprint> ReadFingerprintAsync(PartyGameDbContext db, Guid assetId, string roomCode, bool profilePhoto)
    {
        var asset = await db.MediaAssets.AsNoTracking().SingleAsync(candidate => candidate.Id == assetId);
        return new MediaFingerprint(asset.Id, roomCode, asset.MediaKind, asset.RoomId, asset.PlayerId, asset.QuestionInstanceId,
            asset.DisplayStorageKey, asset.ContentType, asset.ByteLength, asset.Sha256, profilePhoto);
    }

    private static async Task AssertPersistedMetadataAsync(PhotoAnswerTestHarness harness, MediaFingerprint expected)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var actual = await db.MediaAssets.AsNoTracking().SingleAsync(candidate => candidate.Id == expected.Id);
        Assert.Equal(expected.Kind, actual.MediaKind);
        Assert.Equal(expected.RoomId, actual.RoomId);
        Assert.Equal(expected.PlayerId, actual.PlayerId);
        Assert.Equal(expected.QuestionInstanceId, actual.QuestionInstanceId);
        Assert.Equal(expected.DisplayStorageKey, actual.DisplayStorageKey);
        Assert.Equal(expected.ContentType, actual.ContentType);
        Assert.Equal(expected.ByteLength, actual.ByteLength);
        Assert.Equal(expected.Sha256, actual.Sha256);
        Assert.Equal(1, await db.MediaAssets.CountAsync(candidate => candidate.Id == expected.Id));
    }

    private static HubConnection Connection(PhotoAnswerTestHarness harness) => new HubConnectionBuilder()
        .WithUrl("http://localhost/hubs/game", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => harness.Factory.Server.CreateHandler();
        })
        .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
        .Build();

    private static async Task<RoomAccessResponse> CreateAsync(
        PhotoAnswerTestHarness harness,
        string nickname,
        RoomSettingsRequest settings,
        string questionType)
    {
        var response = await harness.Client.PostAsJsonAsync(
            "/api/rooms",
            new CreateRoomRequest(nickname, settings, ["starter"], [questionType]));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!;
    }

    private static async Task<RoomAccessResponse> JoinAsync(PhotoAnswerTestHarness harness, string roomCode, string nickname)
    {
        var response = await harness.Client.PostAsJsonAsync($"/api/rooms/{roomCode}/players", new JoinRoomRequest(nickname));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RoomAccessResponse>(JsonOptions))!;
    }

    private sealed class StartedRoom(
        string roomCode,
        RoomAccessResponse host,
        IReadOnlyList<RoomAccessResponse> players,
        Guid questionInstanceId,
        IReadOnlyList<HubConnection> connections) : IAsyncDisposable
    {
        public string RoomCode { get; } = roomCode;
        public RoomAccessResponse Host { get; } = host;
        public IReadOnlyList<RoomAccessResponse> Players { get; } = players;
        public Guid QuestionInstanceId { get; } = questionInstanceId;

        public async ValueTask DisposeAsync()
        {
            foreach (var connection in connections)
                await connection.DisposeAsync();
        }
    }

    private sealed record MediaFingerprint(
        Guid Id,
        string RoomCode,
        MediaKind Kind,
        Guid RoomId,
        Guid PlayerId,
        Guid? QuestionInstanceId,
        string DisplayStorageKey,
        string ContentType,
        long ByteLength,
        string Sha256,
        bool ProfilePhoto);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
