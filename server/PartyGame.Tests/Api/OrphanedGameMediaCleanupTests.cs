using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class OrphanedGameMediaCleanupTests
{
    [Fact]
    public async Task CleanupUnusedAsync_RemovesOrphanedPhotoAndDrawingAssetsWithBothVariants()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var photoRoom = await harness.CreateRoomAsync();
        await MakeExistingPackageNonConflictingAsync(harness);
        var drawingRoom = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        var photo = await AddOrphanAsync(harness, photoRoom, MediaKind.PhotoAnswer);
        var drawing = await AddOrphanAsync(harness, drawingRoom, MediaKind.DrawingAnswer);

        Assert.Equal(2, await CleanupUnusedAsync(harness));

        Assert.Null(await FindAssetAsync(harness, photo.Id));
        Assert.Null(await FindAssetAsync(harness, drawing.Id));
        Assert.False(File.Exists(PathFor(harness, photo.DisplayStorageKey)));
        Assert.False(File.Exists(PathFor(harness, photo.ThumbnailStorageKey)));
        Assert.False(File.Exists(PathFor(harness, drawing.DisplayStorageKey)));
        Assert.False(File.Exists(PathFor(harness, drawing.ThumbnailStorageKey)));
        Assert.Equal(0, await CleanupUnusedAsync(harness));
    }

    [Fact]
    public async Task CleanupUnusedAsync_PreservesReferencedAnswersAndProfilePhotos()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var photoRoom = await harness.CreateRoomAsync();
        await MakeExistingPackageNonConflictingAsync(harness);
        var drawingRoom = await harness.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
        (await harness.UploadAsync(photoRoom, photoRoom.Players[0], await PhotoAnswerTestHarness.ImageAsync())).EnsureSuccessStatusCode();
        (await harness.UploadDrawingAsync(drawingRoom, drawingRoom.Players[0], await PhotoAnswerTestHarness.DrawingAsync())).EnsureSuccessStatusCode();
        var photo = await FindAssetForQuestionAsync(harness, photoRoom.QuestionInstanceId, MediaKind.PhotoAnswer);
        var drawing = await FindAssetForQuestionAsync(harness, drawingRoom.QuestionInstanceId, MediaKind.DrawingAnswer);
        var profile = await AddProfileAsync(harness, photoRoom);

        Assert.Equal(0, await CleanupUnusedAsync(harness));

        Assert.NotNull(await FindAssetAsync(harness, photo.Id));
        Assert.NotNull(await FindAssetAsync(harness, drawing.Id));
        Assert.NotNull(await FindAssetAsync(harness, profile.Id));
        Assert.True(File.Exists(PathFor(harness, photo.DisplayStorageKey)));
        Assert.True(File.Exists(PathFor(harness, drawing.DisplayStorageKey)));
        Assert.True(File.Exists(PathFor(harness, profile.DisplayStorageKey)));
    }

    [Fact]
    public async Task CleanupUnusedAsync_UsesStableBoundedBatchAndTreatsMissingVariantsAsIdempotent()
    {
        var settings = new Dictionary<string, string?> { ["MediaStorage:OrphanedGameMediaCleanupBatchSize"] = "1" };
        await using var harness = new PhotoAnswerTestHarness(settings: settings);
        var room = await harness.CreateRoomAsync();
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var first = await AddOrphanAsync(harness, room, MediaKind.PhotoAnswer, createdAt, new Guid("00000000-0000-0000-0000-000000000001"));
        var second = await AddOrphanAsync(harness, room, MediaKind.PhotoAnswer, createdAt, new Guid("00000000-0000-0000-0000-000000000002"));
        File.Delete(PathFor(harness, first.DisplayStorageKey));
        File.Delete(PathFor(harness, first.ThumbnailStorageKey));

        Assert.Equal(1, await CleanupUnusedAsync(harness));
        Assert.Null(await FindAssetAsync(harness, first.Id));
        Assert.NotNull(await FindAssetAsync(harness, second.Id));

        File.Delete(PathFor(harness, second.DisplayStorageKey));
        Assert.Equal(1, await CleanupUnusedAsync(harness));
        Assert.Null(await FindAssetAsync(harness, second.Id));
        Assert.Equal(0, await CleanupUnusedAsync(harness));
    }

    [Fact]
    public async Task CleanupAsync_RechecksSubmissionAddedAfterCandidateSelection()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync();
        var orphan = await AddOrphanAsync(harness, room, MediaKind.PhotoAnswer);

        await using (var selectionScope = harness.Factory.Services.CreateAsyncScope())
        {
            var db = selectionScope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            Assert.Equal(orphan.Id, await db.MediaAssets
                .Where(asset => asset.Id == orphan.Id && !db.PhotoAnswerSubmissions.Any(submission => submission.MediaAssetId == asset.Id))
                .Select(asset => asset.Id)
                .SingleAsync());
        }

        await AddPhotoSubmissionAsync(harness, room, orphan.Id);

        await using var cleanupScope = harness.Factory.Services.CreateAsyncScope();
        var cleanup = cleanupScope.ServiceProvider.GetRequiredService<IOrphanedGameMediaCleanupService>();
        Assert.False(await cleanup.CleanupAsync(orphan.Id));
        Assert.NotNull(await FindAssetAsync(harness, orphan.Id));
        Assert.True(File.Exists(PathFor(harness, orphan.DisplayStorageKey)));
    }

    [Fact]
    public async Task FailedCleanup_RetainsRecordWithoutPhysicalPathAndRetriesOnNextStartup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.OrphanedMedia.Retry", Guid.NewGuid().ToString("N"));
        MediaAsset orphan;

        try
        {
            await using (var hostA = new PhotoAnswerTestHarness(directory, deleteOnDispose: false))
            {
                var room = await hostA.CreateRoomAsync();
                orphan = await AddOrphanAsync(hostA, room, MediaKind.PhotoAnswer);
                await using var scope = hostA.Factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<MediaOptions>>();
                var logger = new CapturingLogger();
                var cleanup = new OrphanedGameMediaCleanupService(
                    db,
                    new FailingDeleteStorage(scope.ServiceProvider.GetRequiredService<IMediaStorage>()),
                    options,
                    logger);

                Assert.False(await cleanup.CleanupAsync(orphan.Id));
                Assert.NotNull(await db.MediaAssets.SingleOrDefaultAsync(asset => asset.Id == orphan.Id));
                Assert.DoesNotContain(directory, string.Join('\n', logger.Messages), StringComparison.Ordinal);
            }

            await using (var hostB = new PhotoAnswerTestHarness(directory, deleteOnDispose: false))
            {
                Assert.Null(await FindAssetAsync(hostB, orphan.Id));
                Assert.False(File.Exists(PathFor(hostB, orphan.DisplayStorageKey)));
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Stage6B3_HostRestart_RemovesOrphansAndPreservesReferencedMediaAndProfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.OrphanedMedia.Restart", Guid.NewGuid().ToString("N"));
        MediaAsset orphanPhoto;
        MediaAsset orphanDrawing;
        MediaAsset referencedPhoto;
        MediaAsset referencedDrawing;
        MediaAsset profile;

        try
        {
            await using (var hostA = new PhotoAnswerTestHarness(directory, deleteOnDispose: false))
            {
                var photoRoom = await hostA.CreateRoomAsync();
                await MakeExistingPackageNonConflictingAsync(hostA);
                var drawingRoom = await hostA.CreateRoomAsync(GameStage.CollectingDrawingAnswers, QuestionType.DrawingAnswer);
                (await hostA.UploadAsync(photoRoom, photoRoom.Players[0], await PhotoAnswerTestHarness.ImageAsync())).EnsureSuccessStatusCode();
                (await hostA.UploadDrawingAsync(drawingRoom, drawingRoom.Players[0], await PhotoAnswerTestHarness.DrawingAsync())).EnsureSuccessStatusCode();
                referencedPhoto = await FindAssetForQuestionAsync(hostA, photoRoom.QuestionInstanceId, MediaKind.PhotoAnswer);
                referencedDrawing = await FindAssetForQuestionAsync(hostA, drawingRoom.QuestionInstanceId, MediaKind.DrawingAnswer);
                profile = await AddProfileAsync(hostA, photoRoom);
                orphanPhoto = await AddOrphanAsync(hostA, photoRoom, MediaKind.PhotoAnswer);
                orphanDrawing = await AddOrphanAsync(hostA, drawingRoom, MediaKind.DrawingAnswer);
            }

            await using (var hostB = new PhotoAnswerTestHarness(directory, deleteOnDispose: false))
            {
                Assert.Null(await FindAssetAsync(hostB, orphanPhoto.Id));
                Assert.Null(await FindAssetAsync(hostB, orphanDrawing.Id));
                Assert.NotNull(await FindAssetAsync(hostB, referencedPhoto.Id));
                Assert.NotNull(await FindAssetAsync(hostB, referencedDrawing.Id));
                Assert.NotNull(await FindAssetAsync(hostB, profile.Id));
                Assert.False(File.Exists(PathFor(hostB, orphanPhoto.DisplayStorageKey)));
                Assert.False(File.Exists(PathFor(hostB, orphanDrawing.ThumbnailStorageKey)));
                Assert.True(File.Exists(PathFor(hostB, referencedPhoto.DisplayStorageKey)));
                Assert.True(File.Exists(PathFor(hostB, referencedDrawing.ThumbnailStorageKey)));
                Assert.True(File.Exists(PathFor(hostB, profile.DisplayStorageKey)));
            }

            await using (var hostC = new PhotoAnswerTestHarness(directory, deleteOnDispose: false))
            {
                Assert.Null(await FindAssetAsync(hostC, orphanPhoto.Id));
                Assert.Null(await FindAssetAsync(hostC, orphanDrawing.Id));
                Assert.NotNull(await FindAssetAsync(hostC, referencedPhoto.Id));
                Assert.NotNull(await FindAssetAsync(hostC, referencedDrawing.Id));
                Assert.NotNull(await FindAssetAsync(hostC, profile.Id));
            }
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<int> CleanupUnusedAsync(PhotoAnswerTestHarness harness)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IOrphanedGameMediaCleanupService>().CleanupUnusedAsync();
    }

    private static async Task MakeExistingPackageNonConflictingAsync(PhotoAnswerTestHarness harness)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var package = await db.GamePackages.SingleAsync(candidate => candidate.Key.StartsWith("test-"));
        package.LogicalPackageId = package.Id;
        await db.SaveChangesAsync();
    }

    private static async Task<MediaAsset> AddOrphanAsync(
        PhotoAnswerTestHarness harness,
        PhotoRoomAccess room,
        MediaKind mediaKind,
        DateTimeOffset? createdAt = null,
        Guid? assetId = null)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();
        var writeId = Guid.NewGuid();
        await using var content = new MemoryStream(mediaKind == MediaKind.PhotoAnswer
            ? await PhotoAnswerTestHarness.ImageAsync()
            : await PhotoAnswerTestHarness.DrawingAsync());
        var stored = mediaKind switch
        {
            MediaKind.PhotoAnswer => await storage.SavePhotoAsync(new PhotoMediaWriteRequest(room.RoomId, room.QuestionInstanceId, writeId, content, content.Length, "image/jpeg")),
            MediaKind.DrawingAnswer => await storage.SaveDrawingAsync(new DrawingMediaWriteRequest(room.RoomId, room.QuestionInstanceId, writeId, content, content.Length, "image/png")),
            _ => throw new ArgumentOutOfRangeException(nameof(mediaKind))
        };
        var asset = new MediaAsset
        {
            Id = assetId ?? Guid.NewGuid(),
            MediaKind = mediaKind,
            StorageProvider = "LocalFileSystem",
            RoomId = room.RoomId,
            PlayerId = room.Players[0].PlayerId,
            QuestionInstanceId = room.QuestionInstanceId,
            DisplayStorageKey = stored.DisplayStorageKey,
            ThumbnailStorageKey = stored.ThumbnailStorageKey,
            ContentType = stored.ContentType,
            Width = stored.Width,
            Height = stored.Height,
            ByteLength = stored.ByteLength,
            Sha256 = stored.Sha256,
            CreatedAtUtc = createdAt ?? DateTimeOffset.UtcNow
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    private static async Task<MediaAsset> AddProfileAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();
        var assetId = Guid.NewGuid();
        await using var content = new MemoryStream(await PhotoAnswerTestHarness.ImageAsync());
        var stored = await storage.SaveProfilePhotoAsync(new ProfilePhotoMediaWriteRequest(assetId, room.RoomId, room.Players[0].PlayerId, content, content.Length, "image/jpeg"));
        var asset = new MediaAsset
        {
            Id = assetId,
            MediaKind = MediaKind.ProfilePhoto,
            StorageProvider = "LocalFileSystem",
            RoomId = room.RoomId,
            PlayerId = room.Players[0].PlayerId,
            DisplayStorageKey = stored.DisplayStorageKey,
            ThumbnailStorageKey = stored.ThumbnailStorageKey,
            ContentType = stored.ContentType,
            Width = stored.Width,
            Height = stored.Height,
            ByteLength = stored.ByteLength,
            Sha256 = stored.Sha256,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        var player = await db.Players.SingleAsync(candidate => candidate.Id == room.Players[0].PlayerId);
        player.ProfilePhotoMediaAssetId = asset.Id;
        player.ProfilePhotoContentType = stored.ContentType;
        player.HasProfilePhoto = true;
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        return asset;
    }

    private static async Task AddPhotoSubmissionAsync(PhotoAnswerTestHarness harness, PhotoRoomAccess room, Guid mediaAssetId)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        db.PhotoAnswerSubmissions.Add(new PhotoAnswerSubmission
        {
            Id = Guid.NewGuid(),
            QuestionInstanceId = room.QuestionInstanceId,
            AuthorPlayerId = room.Players[0].PlayerId,
            MediaAssetId = mediaAssetId,
            ClientSubmissionId = Guid.NewGuid(),
            SubmittedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task<MediaAsset> FindAssetForQuestionAsync(PhotoAnswerTestHarness harness, Guid questionInstanceId, MediaKind mediaKind)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().MediaAssets.AsNoTracking()
            .SingleAsync(asset => asset.QuestionInstanceId == questionInstanceId && asset.MediaKind == mediaKind);
    }

    private static async Task<MediaAsset?> FindAssetAsync(PhotoAnswerTestHarness harness, Guid assetId)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().MediaAssets.AsNoTracking()
            .SingleOrDefaultAsync(asset => asset.Id == assetId);
    }

    private static string PathFor(PhotoAnswerTestHarness harness, string storageKey) =>
        MediaStoragePathResolver.ResolveStoragePath(harness.Factory.MediaRootPath, storageKey);

    private sealed class FailingDeleteStorage(IMediaStorage inner) : IMediaStorage
    {
        public Task<StoredMediaResult> SaveProfilePhotoAsync(ProfilePhotoMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SaveProfilePhotoAsync(request, cancellationToken);
        public Task<StoredMediaResult> SavePhotoAsync(PhotoMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SavePhotoAsync(request, cancellationToken);
        public Task<StoredMediaResult> SaveDrawingAsync(DrawingMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SaveDrawingAsync(request, cancellationToken);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => inner.OpenReadAsync(storageKey, cancellationToken);
        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) => inner.ExistsAsync(storageKey, cancellationToken);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) => throw new IOException("Injected deletion failure.");
    }

    private sealed class CapturingLogger : ILogger<OrphanedGameMediaCleanupService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
