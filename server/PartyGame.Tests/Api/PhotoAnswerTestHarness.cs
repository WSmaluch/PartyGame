using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace PartyGame.Tests.Api;

internal sealed class PhotoAnswerTestHarness : IAsyncDisposable
{
    public PhotoAnswerTestHarness(
        string? directory = null,
        bool deleteOnDispose = true,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        Factory = new PartyGameApiFactory(
            directory ?? Path.Combine(Path.GetTempPath(), "PartyGame.PhotoAnswer.Tests", Guid.NewGuid().ToString("N")),
            deleteOnDispose,
            settings,
            configureServices);
        Client = Factory.CreateClient();
    }

    public PartyGameApiFactory Factory { get; }
    public HttpClient Client { get; }

    public async Task<PhotoRoomAccess> CreateRoomAsync(
        GameStage stage = GameStage.CollectingPhotoAnswers,
        QuestionType questionType = QuestionType.PhotoAnswer,
        int playerCount = 3,
        int eligibleCount = 3,
        DateTimeOffset? stageEndsAtUtc = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var sessionService = scope.ServiceProvider.GetRequiredService<IPlayerSessionService>();
        var now = DateTimeOffset.UtcNow;
        var roomId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var roundId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var questionId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N");
        var accesses = new List<PhotoPlayerAccess>();
        var players = new List<Player>();

        for (var index = 0; index < playerCount; index++)
        {
            var token = $"test-token-{Guid.NewGuid():N}";
            var player = new Player
            {
                Id = Guid.NewGuid(),
                RoomId = roomId,
                Nickname = $"Player {index + 1}",
                NormalizedNickname = $"PLAYER {index + 1}",
                IsHost = index == 0,
                IsReady = true,
                IsConnected = true,
                JoinedAtUtc = now,
                LastSeenAtUtc = now,
                Session = new PlayerSession
                {
                    ReconnectTokenHash = sessionService.HashToken(token),
                    CreatedAtUtc = now,
                    ExpiresAtUtc = now.AddDays(1)
                }
            };
            players.Add(player);
            accesses.Add(new PhotoPlayerAccess(player.Id, token, player.Nickname));
        }

        var package = new GamePackage
        {
            Id = packageId,
            Key = $"test-{unique}",
            NamePl = "Test",
            NameEn = "Test",
            DescriptionPl = "Test",
            DescriptionEn = "Test",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var category = new GameCategory
        {
            Id = categoryId,
            PackageId = packageId,
            Package = package,
            Key = $"category-{unique}",
            NamePl = "Test",
            NameEn = "Test"
        };
        var question = new GameQuestion
        {
            Id = questionId,
            CategoryId = categoryId,
            Category = category,
            Key = $"question-{unique}",
            Type = questionType,
            TextPl = "Pytanie testowe?",
            TextEn = "Test question?",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        package.Categories.Add(category);
        category.Questions.Add(question);

        var room = new GameRoom
        {
            Id = roomId,
            Code = NewRoomCode(),
            Phase = RoomPhase.Started,
            StateVersion = 10,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            StartedAtUtc = now,
            HostPlayerId = players[0].Id,
            DisplayConnected = true,
            SelectedPackageKeys = [package.Key],
            EnabledQuestionTypes = [questionType],
            Players = players,
            Settings = new RoomSettings
            {
                GameRoomId = roomId,
                RoundCount = 1,
                QuestionsPerRound = 4,
                PlayerSelectionSeconds = 20,
                TextAnswerSeconds = 40,
                VotingSeconds = 20,
                PhotoSeconds = 45,
                DrawingSeconds = 90,
                ResultPresentationSeconds = 8,
                FinalRoundEnabled = false,
                FinalDrawingPasses = 3
            }
        };
        foreach (var player in players) player.Room = room;

        var gameSession = new GameSession
        {
            Id = sessionId,
            RoomId = roomId,
            Room = room,
            Stage = stage,
            CurrentRoundNumber = 1,
            TotalRounds = 1,
            CurrentQuestionNumber = 1,
            QuestionsInCurrentRound = 1,
            CurrentCategoryId = categoryId,
            CurrentQuestionInstanceId = instanceId,
            StartedAtUtc = now,
            StageStartedAtUtc = now,
            StageEndsAtUtc = stageEndsAtUtc ?? now.AddMinutes(5)
        };
        var round = new GameRound
        {
            Id = roundId,
            GameSessionId = sessionId,
            Session = gameSession,
            RoundNumber = 1,
            CategoryId = categoryId,
            Category = category,
            StartedAtUtc = now
        };
        var instance = new GameQuestionInstance
        {
            Id = instanceId,
            RoundId = roundId,
            Round = round,
            QuestionId = questionId,
            Question = question,
            QuestionNumber = 1,
            Stage = stage,
            StartedAtUtc = now,
            AnsweringStartedAtUtc = now,
            AnsweringEndsAtUtc = stageEndsAtUtc ?? now.AddMinutes(5)
        };
        for (var index = 0; index < Math.Min(eligibleCount, players.Count); index++)
        {
            instance.PhotoAnswerEligiblePlayers.Add(new PhotoAnswerEligiblePlayer
            {
                Id = Guid.NewGuid(),
                QuestionInstanceId = instanceId,
                PlayerId = players[index].Id
            });
            if (stage is GameStage.CollectingPhotoAnswerVotes or GameStage.ShowingPhotoAnswerResults)
            {
                instance.PhotoAnswerVoteEligiblePlayers.Add(new PhotoAnswerVoteEligiblePlayer
                {
                    Id = Guid.NewGuid(),
                    QuestionInstanceId = instanceId,
                    PlayerId = players[index].Id
                });
            }
            if (questionType == QuestionType.DrawingAnswer)
            {
                instance.DrawingAnswerEligiblePlayers.Add(new DrawingAnswerEligiblePlayer { Id = Guid.NewGuid(), QuestionInstanceId = instanceId, PlayerId = players[index].Id });
                if (stage is GameStage.CollectingDrawingAnswerVotes or GameStage.ShowingDrawingAnswerResults)
                    instance.DrawingAnswerVoteEligiblePlayers.Add(new DrawingAnswerVoteEligiblePlayer { Id = Guid.NewGuid(), QuestionInstanceId = instanceId, PlayerId = players[index].Id });
            }
        }
        round.Questions.Add(instance);
        gameSession.Rounds.Add(round);
        room.Session = gameSession;

        db.GamePackages.Add(package);
        db.GameRooms.Add(room);
        await db.SaveChangesAsync();
        return new PhotoRoomAccess(room.Code, roomId, sessionId, instanceId, question.Key, accesses);
    }

    public async Task<HttpResponseMessage> UploadAsync(
        PhotoRoomAccess room,
        PhotoPlayerAccess player,
        byte[]? bytes,
        string contentType = "image/jpeg",
        Guid? clientSubmissionId = null,
        Guid? questionInstanceId = null,
        string? roomCode = null,
        string? token = null,
        Guid? playerId = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent((playerId ?? player.PlayerId).ToString()), "playerId");
        form.Add(new StringContent(token ?? player.Token), "reconnectToken");
        form.Add(new StringContent((clientSubmissionId ?? Guid.NewGuid()).ToString()), "clientSubmissionId");
        if (bytes is not null)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(file, "photo", contentType == "image/png" ? "answer.png" : "answer.jpg");
        }
        return await Client.PostAsync(
            $"/api/rooms/{roomCode ?? room.RoomCode}/questions/{questionInstanceId ?? room.QuestionInstanceId}/photo-answers",
            form);
    }

    public static async Task<byte[]> ImageAsync(int width = 640, int height = 480, bool png = false)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(25, 80, 160));
        await using var stream = new MemoryStream();
        if (png) await image.SaveAsync(stream, new PngEncoder());
        else await image.SaveAsync(stream, new JpegEncoder());
        return stream.ToArray();
    }

    public async Task<HttpResponseMessage> UploadDrawingAsync(PhotoRoomAccess room, PhotoPlayerAccess player, byte[]? bytes, string contentType = "image/png", Guid? clientSubmissionId = null, Guid? questionInstanceId = null, string? roomCode = null, string? token = null, Guid? playerId = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent((playerId ?? player.PlayerId).ToString()), "playerId");
        form.Add(new StringContent(token ?? player.Token), "reconnectToken");
        form.Add(new StringContent((clientSubmissionId ?? Guid.NewGuid()).ToString()), "clientSubmissionId");
        if (bytes is not null)
        {
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
            form.Add(file, "drawing", "drawing.png");
        }
        return await Client.PostAsync($"/api/rooms/{roomCode ?? room.RoomCode}/questions/{questionInstanceId ?? room.QuestionInstanceId}/drawing-answers", form);
    }

    public static async Task<byte[]> DrawingAsync(int width = 640, int height = 480, bool transparent = false, bool drawLine = true)
    {
        using var image = new Image<Rgba32>(width, height, transparent ? Color.Transparent : Color.White);
        if (drawLine) for (var y = 0; y < height; y++) for (var x = width / 2 - 1; x <= width / 2; x++) image[x, y] = new Rgba32(20, 80, 220, transparent ? (byte)160 : (byte)255);
        await using var stream = new MemoryStream();
        await image.SaveAsync(stream, new PngEncoder());
        return stream.ToArray();
    }

    public async Task<(int Submissions, int Assets, int Votes, int Scores, long Version)> DrawingCountsAsync(string roomCode)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var room = await db.GameRooms.AsNoTracking().SingleAsync(candidate => candidate.Code == roomCode);
        return (await db.DrawingAnswerSubmissions.CountAsync(), await db.MediaAssets.CountAsync(), await db.DrawingAnswerVotes.CountAsync(), await db.ScoreTransactions.CountAsync(), room.StateVersion);
    }

    public int FinalPngCount() => Directory.Exists(Factory.MediaRootPath) ? Directory.EnumerateFiles(Factory.MediaRootPath, "*.png", SearchOption.AllDirectories).Count() : 0;

    public async Task<(int Submissions, int Assets, int Votes, int Scores, long Version)> CountsAsync(string roomCode)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var room = await db.GameRooms.AsNoTracking().SingleAsync(candidate => candidate.Code == roomCode);
        return (
            await db.PhotoAnswerSubmissions.CountAsync(),
            await db.MediaAssets.CountAsync(),
            await db.PhotoAnswerVotes.CountAsync(),
            await db.ScoreTransactions.CountAsync(),
            room.StateVersion);
    }

    public async Task<GameStage> ProcessAtAsync(PhotoRoomAccess room, DateTimeOffset now)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var stateMachine = scope.ServiceProvider.GetRequiredService<GameStateMachine>();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        await stateMachine.ProcessTransitionAsync(room.GameSessionId, now, CancellationToken.None);
        var trackedRoom = await db.GameRooms.SingleAsync(candidate => candidate.Id == room.RoomId);
        trackedRoom.PublicStateChanged(now);
        await db.SaveChangesAsync();
        return await db.GameSessions.AsNoTracking().Where(candidate => candidate.Id == room.GameSessionId).Select(candidate => candidate.Stage).SingleAsync();
    }

    public int FinalJpegCount() => Directory.Exists(Factory.MediaRootPath)
        ? Directory.EnumerateFiles(Factory.MediaRootPath, "*.jpg", SearchOption.AllDirectories).Count()
        : 0;

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
    }

    private static string NewRoomCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = Guid.NewGuid().ToByteArray();
        return string.Concat(bytes.Take(4).Select(value => alphabet[value % alphabet.Length]));
    }
}

internal sealed record PhotoPlayerAccess(Guid PlayerId, string Token, string Nickname);
internal sealed record PhotoRoomAccess(
    string RoomCode,
    Guid RoomId,
    Guid GameSessionId,
    Guid QuestionInstanceId,
    string QuestionKey,
    IReadOnlyList<PhotoPlayerAccess> Players);
