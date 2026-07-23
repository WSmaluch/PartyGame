using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Tests.Infrastructure.Media;

namespace PartyGame.Tests.Api;

public sealed class UntrackedMediaFileCleanupTests
{
    [Fact]
    public async Task CleanupAsync_RespectsMinimumAndMaximumBatchAndContinuesAfterFailure()
    {
        await using var harness = new PhotoAnswerTestHarness();
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var now = DateTimeOffset.UtcNow;
        var entries = Enumerable.Range(0, 105)
            .Select(index => new LocalMediaFileEntry($"candidate-{index:D3}", now.AddHours(-2)))
            .ToList();
        var catalog = new FakeCatalog(entries) { FailingKey = entries[0].StorageKey };
        var logger = new CapturingLogger();
        var cleanup = Service(
            db,
            catalog,
            now,
            batchSize: 1000,
            logger);

        var result = await cleanup.CleanupAsync();

        Assert.Equal(100, result.Candidates);
        Assert.Equal(99, result.Deleted);
        Assert.Equal(1, result.Failed);
        Assert.Equal(6, catalog.Remaining.Count);
        Assert.DoesNotContain(harness.Factory.MediaRootPath, string.Join('\n', logger.Messages), StringComparison.Ordinal);

        var minimumCatalog = new FakeCatalog(entries.Take(2));
        var minimum = Service(db, minimumCatalog, now, batchSize: 0, new CapturingLogger());
        Assert.Equal(1, (await minimum.CleanupAsync()).Candidates);
    }

    [Fact]
    public async Task CleanupAsync_RechecksDatabaseImmediatelyBeforeDelete()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var room = await harness.CreateRoomAsync();
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var now = DateTimeOffset.UtcNow;
        var key = LocalMediaFileCatalogTests.AnswerKey(
            "photo-answer",
            room.RoomId,
            room.QuestionInstanceId,
            Guid.NewGuid(),
            "display",
            ".jpg");
        var thumbnailKey = key.Replace("display.jpg", "thumbnail.jpg", StringComparison.Ordinal);
        var catalog = new FakeCatalog([new LocalMediaFileEntry(key, now.AddHours(-2))]);
        catalog.BeforeInspectAsync = async () =>
        {
            db.MediaAssets.Add(new MediaAsset
            {
                Id = Guid.NewGuid(),
                MediaKind = MediaKind.PhotoAnswer,
                StorageProvider = "LocalFileSystem",
                RoomId = room.RoomId,
                PlayerId = room.Players[0].PlayerId,
                QuestionInstanceId = room.QuestionInstanceId,
                DisplayStorageKey = key,
                ThumbnailStorageKey = thumbnailKey,
                ContentType = "image/jpeg",
                Width = 640,
                Height = 480,
                ByteLength = 1,
                Sha256 = new string('a', 64),
                CreatedAtUtc = now
            });
            await db.SaveChangesAsync();
        };
        var cleanup = Service(db, catalog, now, 25, new CapturingLogger());

        var result = await cleanup.CleanupAsync();

        Assert.Equal(1, result.Candidates);
        Assert.Equal(1, result.SkippedReferenced);
        Assert.Equal(0, result.Deleted);
        Assert.Contains(key, catalog.Remaining);
    }

    [Fact]
    public async Task CleanupAsync_RechecksAgeAndTreatsMissingDeleteAsIdempotent()
    {
        await using var harness = new PhotoAnswerTestHarness();
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var now = DateTimeOffset.UtcNow;
        var refreshed = new LocalMediaFileEntry("refreshed", now.AddHours(-2));
        var missing = new LocalMediaFileEntry("missing", now.AddHours(-2));
        var catalog = new FakeCatalog([refreshed, missing])
        {
            RefreshedKey = refreshed.StorageKey,
            MissingOnDeleteKey = missing.StorageKey,
            RefreshTime = now
        };
        var cleanup = Service(db, catalog, now, 25, new CapturingLogger());

        var result = await cleanup.CleanupAsync();

        Assert.Equal(2, result.Candidates);
        Assert.Equal(1, result.SkippedTooYoung);
        Assert.Equal(1, result.Missing);
        Assert.Equal(0, result.Failed);
        Assert.Equal([refreshed.StorageKey], catalog.Remaining);
        Assert.Equal(0, (await cleanup.CleanupAsync()).Candidates);
    }

    [Fact]
    public async Task CleanupAsync_RealCatalogPreservesReferencedAndFreshFilesAndIsIdempotent()
    {
        var settings = Settings(batchSize: 25);
        await using var harness = new PhotoAnswerTestHarness(settings: settings);
        var room = await harness.CreateRoomAsync();
        (await harness.UploadAsync(
            room,
            room.Players[0],
            await PhotoAnswerTestHarness.ImageAsync())).EnsureSuccessStatusCode();
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var referenced = await db.MediaAssets.AsNoTracking()
            .SingleAsync(asset => asset.MediaKind == MediaKind.PhotoAnswer);
        SetOld(harness.Factory.MediaRootPath, referenced.DisplayStorageKey);
        SetOld(harness.Factory.MediaRootPath, referenced.ThumbnailStorageKey);
        var oldKey = LocalMediaFileCatalogTests.ProfileKey(
            room.RoomId,
            room.Players[0].PlayerId,
            Guid.NewGuid(),
            "display");
        var freshKey = LocalMediaFileCatalogTests.ProfileKey(
            room.RoomId,
            room.Players[0].PlayerId,
            Guid.NewGuid(),
            "thumbnail");
        CreateFile(harness.Factory.MediaRootPath, oldKey, old: true);
        CreateFile(harness.Factory.MediaRootPath, freshKey, old: false);
        var cleanup = scope.ServiceProvider.GetRequiredService<IUntrackedMediaFileCleanupService>();

        var first = await cleanup.CleanupAsync();
        var second = await cleanup.CleanupAsync();

        Assert.Equal(1, first.Deleted);
        Assert.True(first.SkippedReferenced >= 2);
        Assert.True(first.SkippedTooYoung >= 1);
        Assert.False(Exists(harness.Factory.MediaRootPath, oldKey));
        Assert.True(Exists(harness.Factory.MediaRootPath, freshKey));
        Assert.True(Exists(harness.Factory.MediaRootPath, referenced.DisplayStorageKey));
        Assert.True(Exists(harness.Factory.MediaRootPath, referenced.ThumbnailStorageKey));
        Assert.Equal(0, second.Deleted);
    }

    [Fact]
    public async Task CleanupAsync_RealCatalogUsesStableCandidateOrderAndBatch()
    {
        var settings = Settings(batchSize: 2);
        await using var harness = new PhotoAnswerTestHarness(settings: settings);
        var keys = new[]
        {
            LocalMediaFileCatalogTests.ProfileKey(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "display"),
            LocalMediaFileCatalogTests.AnswerKey("photo-answer", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "display", ".jpg"),
            LocalMediaFileCatalogTests.AnswerKey("drawing-answer", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "display", ".png")
        };
        foreach (var key in keys)
            CreateFile(harness.Factory.MediaRootPath, key, old: true);

        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<IUntrackedMediaFileCleanupService>();

        Assert.Equal(2, (await cleanup.CleanupAsync()).Deleted);
        Assert.False(Exists(harness.Factory.MediaRootPath, keys.Single(key => key.StartsWith("drawing-answer", StringComparison.Ordinal))));
        Assert.False(Exists(harness.Factory.MediaRootPath, keys.Single(key => key.StartsWith("photo-answer", StringComparison.Ordinal))));
        Assert.True(Exists(harness.Factory.MediaRootPath, keys.Single(key => key.StartsWith("profile", StringComparison.Ordinal))));
        Assert.Equal(1, (await cleanup.CleanupAsync()).Deleted);
    }

    internal static IReadOnlyDictionary<string, string?> Settings(int batchSize = 25) =>
        new Dictionary<string, string?>
        {
            ["MediaStorage:UntrackedFileCleanupBatchSize"] = batchSize.ToString(),
            ["MediaStorage:UntrackedFileCleanupGracePeriodMinutes"] = "60"
        };

    internal static void CreateFile(string root, string key, bool old)
    {
        var path = MediaStoragePathResolver.ResolveStoragePath(root, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, key);
        if (old)
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
    }

    internal static bool Exists(string root, string key) =>
        File.Exists(MediaStoragePathResolver.ResolveStoragePath(root, key));

    internal static void SetOld(string root, string key) =>
        File.SetLastWriteTimeUtc(
            MediaStoragePathResolver.ResolveStoragePath(root, key),
            DateTime.UtcNow.AddHours(-2));

    private static UntrackedMediaFileCleanupService Service(
        PartyGameDbContext db,
        ILocalMediaFileCatalog catalog,
        DateTimeOffset now,
        int batchSize,
        ILogger<UntrackedMediaFileCleanupService> logger) =>
        new(
            db,
            catalog,
            Options.Create(new MediaOptions
            {
                UntrackedFileCleanupEnabled = true,
                UntrackedFileCleanupBatchSize = batchSize,
                UntrackedFileCleanupGracePeriodMinutes = 60
            }),
            new FixedClock(now),
            logger);

    private sealed class FixedClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeCatalog(IEnumerable<LocalMediaFileEntry> entries) : ILocalMediaFileCatalog
    {
        private readonly Dictionary<string, LocalMediaFileEntry> files = entries
            .ToDictionary(entry => entry.StorageKey, StringComparer.Ordinal);
        private bool beforeInspectInvoked;

        public string? FailingKey { get; init; }
        public string? MissingOnDeleteKey { get; init; }
        public string? RefreshedKey { get; init; }
        public DateTimeOffset RefreshTime { get; init; }
        public Func<Task>? BeforeInspectAsync { get; set; }
        public IReadOnlyCollection<string> Remaining => files.Keys;

        public IEnumerable<LocalMediaFileEntry> EnumerateFinalFiles(CancellationToken cancellationToken = default) =>
            files.Values.OrderBy(entry => entry.StorageKey, StringComparer.Ordinal).ToList();

        public async Task<LocalMediaFileEntry?> GetFinalFileAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            if (!beforeInspectInvoked && BeforeInspectAsync is not null)
            {
                beforeInspectInvoked = true;
                await BeforeInspectAsync();
            }

            if (!files.TryGetValue(storageKey, out var entry))
                return null;

            if (storageKey == RefreshedKey)
            {
                entry = entry with { LastWriteTimeUtc = RefreshTime };
                files[storageKey] = entry;
            }

            return entry;
        }

        public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
        {
            if (storageKey == FailingKey)
                throw new IOException("Injected failure at /physical/root/path.");

            if (storageKey == MissingOnDeleteKey)
            {
                files.Remove(storageKey);
                return Task.FromResult(false);
            }

            return Task.FromResult(files.Remove(storageKey));
        }
    }

    private sealed class CapturingLogger : ILogger<UntrackedMediaFileCleanupService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
