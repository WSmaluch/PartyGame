using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartyGame.Domain.Game;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Infrastructure.Media;

public sealed class ProfilePhotoCleanupServiceTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PartyGame.ProfilePhotoCleanup", Guid.NewGuid().ToString("N"));
    private PartyGameDbContext db = null!;
    private MediaOptions options = null!;
    private LocalMediaStorage storage = null!;
    private GameRoom room = null!;
    private Player player = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        db = new PartyGameDbContext(new DbContextOptionsBuilder<PartyGameDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "test.db")}")
            .Options);
        await db.Database.MigrateAsync();
        options = new MediaOptions { RootPath = Path.Combine(directory, "media"), ProfilePhotoCleanupBatchSize = 25 };
        storage = new LocalMediaStorage(Options.Create(options));

        var now = DateTimeOffset.UtcNow;
        room = new GameRoom
        {
            Id = Guid.NewGuid(),
            Code = "CLNP",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            HostPlayerId = Guid.NewGuid(),
            Settings = new RoomSettings()
        };
        room.Settings.GameRoomId = room.Id;
        player = new Player
        {
            Id = room.HostPlayerId,
            RoomId = room.Id,
            Room = room,
            Nickname = "Cleanup",
            NormalizedNickname = "CLEANUP",
            IsHost = true,
            JoinedAtUtc = now,
            LastSeenAtUtc = now,
            Session = new PlayerSession
            {
                ReconnectTokenHash = new string('a', 64),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(1)
            }
        };
        db.AddRange(room, player);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public async Task CleanupAsync_RemovesInactiveProfileAssetAndBothVariants()
    {
        var asset = await AddAssetAsync(MediaKind.ProfilePhoto, active: false);
        var cleanup = CreateCleanup(storage);

        var removed = await cleanup.CleanupAsync(asset.Id);

        Assert.True(removed);
        Assert.False(File.Exists(PathFor(asset.DisplayStorageKey)));
        Assert.False(File.Exists(PathFor(asset.ThumbnailStorageKey)));
        Assert.Null(await db.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == asset.Id));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task CleanupAsync_MissingVariantsRemainIdempotent(bool missingDisplay, bool missingThumbnail)
    {
        var asset = await AddAssetAsync(MediaKind.ProfilePhoto, active: false);
        if (missingDisplay) File.Delete(PathFor(asset.DisplayStorageKey));
        if (missingThumbnail) File.Delete(PathFor(asset.ThumbnailStorageKey));

        var cleanup = CreateCleanup(storage);

        Assert.True(await cleanup.CleanupAsync(asset.Id));
        Assert.False(await cleanup.CleanupAsync(asset.Id));
        Assert.Null(await db.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == asset.Id));
    }

    [Fact]
    public async Task CleanupAsync_NeverRemovesActiveOrNonProfileAssets()
    {
        var activeProfile = await AddAssetAsync(MediaKind.ProfilePhoto, active: true);
        var photoAnswer = await AddAssetAsync(MediaKind.PhotoAnswer, active: false);
        var drawingAnswer = await AddAssetAsync(MediaKind.DrawingAnswer, active: false);
        var cleanup = CreateCleanup(storage);

        Assert.False(await cleanup.CleanupAsync(activeProfile.Id));
        Assert.False(await cleanup.CleanupAsync(photoAnswer.Id));
        Assert.False(await cleanup.CleanupAsync(drawingAnswer.Id));

        Assert.True(File.Exists(PathFor(activeProfile.DisplayStorageKey)));
        Assert.NotNull(await db.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == activeProfile.Id));
        Assert.NotNull(await db.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == photoAnswer.Id));
        Assert.NotNull(await db.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == drawingAnswer.Id));
    }

    [Fact]
    public async Task CleanupAsync_FailedFileDeletionRetainsRecordForRetryWithoutLoggingPhysicalPath()
    {
        var asset = await AddAssetAsync(MediaKind.ProfilePhoto, active: false);
        var logger = new CapturingLogger();
        var failingStorage = new FailingDeleteStorage(storage) { FailDeletes = true };
        var cleanup = CreateCleanup(failingStorage, logger);

        Assert.False(await cleanup.CleanupAsync(asset.Id));
        Assert.NotNull(await db.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == asset.Id));
        Assert.DoesNotContain(directory, string.Join('\n', logger.Messages), StringComparison.Ordinal);

        failingStorage.FailDeletes = false;
        Assert.Equal(1, await cleanup.CleanupUnusedAsync());
        Assert.Null(await db.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == asset.Id));
    }

    [Fact]
    public async Task CleanupUnusedAsync_UsesStableBoundedProfileOnlyBatch()
    {
        options.ProfilePhotoCleanupBatchSize = 1;
        var older = await AddAssetAsync(MediaKind.ProfilePhoto, active: false, createdAt: DateTimeOffset.UtcNow.AddMinutes(-2));
        var newer = await AddAssetAsync(MediaKind.ProfilePhoto, active: false, createdAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var answer = await AddAssetAsync(MediaKind.PhotoAnswer, active: false, createdAt: DateTimeOffset.UtcNow.AddMinutes(-3));
        var cleanup = CreateCleanup(storage);

        Assert.Equal(1, await cleanup.CleanupUnusedAsync());
        Assert.Null(await db.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == older.Id));
        Assert.NotNull(await db.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == newer.Id));
        Assert.NotNull(await db.MediaAssets.SingleOrDefaultAsync(candidate => candidate.Id == answer.Id));
    }

    private ProfilePhotoCleanupService CreateCleanup(IMediaStorage targetStorage, ILogger<ProfilePhotoCleanupService>? logger = null) =>
        new(db, targetStorage, Options.Create(options), logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProfilePhotoCleanupService>.Instance);

    private async Task<MediaAsset> AddAssetAsync(MediaKind mediaKind, bool active, DateTimeOffset? createdAt = null)
    {
        var id = Guid.NewGuid();
        var asset = new MediaAsset
        {
            Id = id,
            MediaKind = mediaKind,
            StorageProvider = "LocalFileSystem",
            RoomId = room.Id,
            PlayerId = player.Id,
            DisplayStorageKey = $"cleanup/{id:N}/display.jpg",
            ThumbnailStorageKey = $"cleanup/{id:N}/thumbnail.jpg",
            ContentType = "image/jpeg",
            Width = 640,
            Height = 480,
            ByteLength = 1,
            Sha256 = new string('a', 64),
            CreatedAtUtc = createdAt ?? DateTimeOffset.UtcNow
        };
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
        await File.WriteAllTextAsync(PathFor(asset.DisplayStorageKey), "display");
        await File.WriteAllTextAsync(PathFor(asset.ThumbnailStorageKey), "thumbnail");

        if (active)
        {
            player.ProfilePhotoMediaAssetId = asset.Id;
            player.HasProfilePhoto = true;
            await db.SaveChangesAsync();
        }

        return asset;
    }

    private string PathFor(string storageKey)
    {
        var root = MediaStoragePathResolver.ResolveRootPath(options.RootPath);
        var path = MediaStoragePathResolver.ResolveStoragePath(root, storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private sealed class FailingDeleteStorage(IMediaStorage inner) : IMediaStorage
    {
        public bool FailDeletes { get; set; }
        public Task<StoredMediaResult> SaveProfilePhotoAsync(ProfilePhotoMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SaveProfilePhotoAsync(request, cancellationToken);
        public Task<StoredMediaResult> SavePhotoAsync(PhotoMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SavePhotoAsync(request, cancellationToken);
        public Task<StoredMediaResult> SaveDrawingAsync(DrawingMediaWriteRequest request, CancellationToken cancellationToken = default) => inner.SaveDrawingAsync(request, cancellationToken);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => inner.OpenReadAsync(storageKey, cancellationToken);
        public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) => inner.ExistsAsync(storageKey, cancellationToken);
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            FailDeletes ? throw new IOException("Injected deletion failure.") : inner.DeleteAsync(storageKey, cancellationToken);
    }

    private sealed class CapturingLogger : ILogger<ProfilePhotoCleanupService>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
