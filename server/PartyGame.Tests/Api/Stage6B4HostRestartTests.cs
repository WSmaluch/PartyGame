using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Tests.Infrastructure.Media;

namespace PartyGame.Tests.Api;

public sealed class Stage6B4HostRestartTests
{
    [Fact]
    public async Task HostRestart_RemovesOnlyOldRecognizedFilesWithoutMediaAsset()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "PartyGame.Stage6B4.Restart",
            Guid.NewGuid().ToString("N"));
        var settings = UntrackedMediaFileCleanupTests.Settings();
        List<MediaAssetSnapshot> referenced;
        List<string> oldUntracked;
        List<string> freshUntracked;
        string temporaryKey;
        string unknownKey;
        DatabaseCounts expectedCounts;

        try
        {
            await using (var hostA = new PhotoAnswerTestHarness(
                             directory,
                             deleteOnDispose: false,
                             settings: settings))
            {
                var photoRoom = await hostA.CreateRoomAsync();
                (await hostA.UploadAsync(
                    photoRoom,
                    photoRoom.Players[0],
                    await PhotoAnswerTestHarness.ImageAsync())).EnsureSuccessStatusCode();
                await AddProfileAsync(hostA, photoRoom);
                await MakeExistingPackageNonConflictingAsync(hostA);
                var drawingRoom = await hostA.CreateRoomAsync(
                    GameStage.CollectingDrawingAnswers,
                    QuestionType.DrawingAnswer);
                (await hostA.UploadDrawingAsync(
                    drawingRoom,
                    drawingRoom.Players[0],
                    await PhotoAnswerTestHarness.DrawingAsync())).EnsureSuccessStatusCode();

                await using (var scope = hostA.Factory.Services.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
                    referenced = await db.MediaAssets.AsNoTracking()
                        .OrderBy(asset => asset.MediaKind)
                        .Select(asset => new MediaAssetSnapshot(
                            asset.Id,
                            asset.DisplayStorageKey,
                            asset.ThumbnailStorageKey))
                        .ToListAsync();
                    expectedCounts = await CountsAsync(db);
                }

                Assert.Equal(3, referenced.Count);
                foreach (var asset in referenced)
                {
                    UntrackedMediaFileCleanupTests.SetOld(
                        hostA.Factory.MediaRootPath,
                        asset.DisplayStorageKey);
                    UntrackedMediaFileCleanupTests.SetOld(
                        hostA.Factory.MediaRootPath,
                        asset.ThumbnailStorageKey);
                }

                oldUntracked =
                [
                    .. Pair(LocalMediaFileCatalogTests.ProfileKey(
                        photoRoom.RoomId,
                        photoRoom.Players[0].PlayerId,
                        Guid.NewGuid(),
                        "display")),
                    .. Pair(LocalMediaFileCatalogTests.AnswerKey(
                        "photo-answer",
                        photoRoom.RoomId,
                        photoRoom.QuestionInstanceId,
                        Guid.NewGuid(),
                        "display",
                        ".jpg")),
                    .. Pair(LocalMediaFileCatalogTests.AnswerKey(
                        "drawing-answer",
                        drawingRoom.RoomId,
                        drawingRoom.QuestionInstanceId,
                        Guid.NewGuid(),
                        "display",
                        ".png"))
                ];
                foreach (var key in oldUntracked)
                    UntrackedMediaFileCleanupTests.CreateFile(hostA.Factory.MediaRootPath, key, old: true);

                freshUntracked =
                [
                    LocalMediaFileCatalogTests.ProfileKey(
                        photoRoom.RoomId,
                        photoRoom.Players[0].PlayerId,
                        Guid.NewGuid(),
                        "display"),
                    LocalMediaFileCatalogTests.AnswerKey(
                        "photo-answer",
                        photoRoom.RoomId,
                        photoRoom.QuestionInstanceId,
                        Guid.NewGuid(),
                        "thumbnail",
                        ".jpg"),
                    LocalMediaFileCatalogTests.AnswerKey(
                        "drawing-answer",
                        drawingRoom.RoomId,
                        drawingRoom.QuestionInstanceId,
                        Guid.NewGuid(),
                        "display",
                        ".png")
                ];
                foreach (var key in freshUntracked)
                    UntrackedMediaFileCleanupTests.CreateFile(hostA.Factory.MediaRootPath, key, old: false);

                temporaryKey = $".tmp/{Guid.NewGuid():N}.tmp";
                unknownKey = $"photo-answer/rooms/{photoRoom.RoomId:N}/questions/{photoRoom.QuestionInstanceId:N}/{Guid.NewGuid():N}/preview.jpg";
                UntrackedMediaFileCleanupTests.CreateFile(hostA.Factory.MediaRootPath, temporaryKey, old: false);
                UntrackedMediaFileCleanupTests.CreateFile(hostA.Factory.MediaRootPath, unknownKey, old: true);
            }

            await using (var hostB = new PhotoAnswerTestHarness(
                             directory,
                             deleteOnDispose: false,
                             settings: settings))
            {
                Assert.All(oldUntracked, key =>
                    Assert.False(UntrackedMediaFileCleanupTests.Exists(hostB.Factory.MediaRootPath, key)));
                Assert.All(freshUntracked, key =>
                    Assert.True(UntrackedMediaFileCleanupTests.Exists(hostB.Factory.MediaRootPath, key)));
                Assert.True(UntrackedMediaFileCleanupTests.Exists(hostB.Factory.MediaRootPath, temporaryKey));
                Assert.True(UntrackedMediaFileCleanupTests.Exists(hostB.Factory.MediaRootPath, unknownKey));
                Assert.All(referenced, asset =>
                {
                    Assert.True(UntrackedMediaFileCleanupTests.Exists(hostB.Factory.MediaRootPath, asset.DisplayStorageKey));
                    Assert.True(UntrackedMediaFileCleanupTests.Exists(hostB.Factory.MediaRootPath, asset.ThumbnailStorageKey));
                });
                await AssertCountsAsync(hostB, expectedCounts);
            }

            await using (var hostC = new PhotoAnswerTestHarness(
                             directory,
                             deleteOnDispose: false,
                             settings: settings))
            {
                Assert.All(oldUntracked, key =>
                    Assert.False(UntrackedMediaFileCleanupTests.Exists(hostC.Factory.MediaRootPath, key)));
                Assert.All(freshUntracked, key =>
                    Assert.True(UntrackedMediaFileCleanupTests.Exists(hostC.Factory.MediaRootPath, key)));
                Assert.True(UntrackedMediaFileCleanupTests.Exists(hostC.Factory.MediaRootPath, temporaryKey));
                Assert.True(UntrackedMediaFileCleanupTests.Exists(hostC.Factory.MediaRootPath, unknownKey));
                Assert.All(referenced, asset =>
                {
                    Assert.True(UntrackedMediaFileCleanupTests.Exists(hostC.Factory.MediaRootPath, asset.DisplayStorageKey));
                    Assert.True(UntrackedMediaFileCleanupTests.Exists(hostC.Factory.MediaRootPath, asset.ThumbnailStorageKey));
                });
                await AssertCountsAsync(hostC, expectedCounts);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static IEnumerable<string> Pair(string displayKey)
    {
        yield return displayKey;
        yield return displayKey.Replace("display.", "thumbnail.", StringComparison.Ordinal);
    }

    private static async Task AddProfileAsync(
        PhotoAnswerTestHarness harness,
        PhotoRoomAccess room)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();
        var assetId = Guid.NewGuid();
        await using var content = new MemoryStream(await PhotoAnswerTestHarness.ImageAsync());
        var stored = await storage.SaveProfilePhotoAsync(new ProfilePhotoMediaWriteRequest(
            assetId,
            room.RoomId,
            room.Players[0].PlayerId,
            content,
            content.Length,
            "image/jpeg"));
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
        var player = await db.Players.SingleAsync(
            candidate => candidate.Id == room.Players[0].PlayerId);
        player.ProfilePhotoMediaAssetId = asset.Id;
        player.ProfilePhotoContentType = stored.ContentType;
        player.HasProfilePhoto = true;
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
    }

    private static async Task MakeExistingPackageNonConflictingAsync(
        PhotoAnswerTestHarness harness)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var package = await db.GamePackages.SingleAsync(
            candidate => candidate.Key.StartsWith("test-"));
        package.LogicalPackageId = package.Id;
        await db.SaveChangesAsync();
    }

    private static async Task<DatabaseCounts> CountsAsync(PartyGameDbContext db) =>
        new(
            await db.MediaAssets.CountAsync(),
            await db.PhotoAnswerSubmissions.CountAsync(),
            await db.DrawingAnswerSubmissions.CountAsync());

    private static async Task AssertCountsAsync(
        PhotoAnswerTestHarness harness,
        DatabaseCounts expected)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var actual = await CountsAsync(
            scope.ServiceProvider.GetRequiredService<PartyGameDbContext>());
        Assert.Equal(expected, actual);
    }

    private sealed record MediaAssetSnapshot(
        Guid Id,
        string DisplayStorageKey,
        string ThumbnailStorageKey);

    private sealed record DatabaseCounts(
        int Assets,
        int PhotoSubmissions,
        int DrawingSubmissions);
}
