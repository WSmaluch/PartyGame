using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace PartyGame.Tests.Infrastructure.Media;

public sealed class BackfillProfilePhotosTests : IAsyncLifetime
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PartyGame.BackfillTests", Guid.NewGuid().ToString("N"));
    private readonly string relativeRoot = Path.Combine("backfill-tests", Guid.NewGuid().ToString("N"));
    private PartyGameDbContext db = null!;
    private MediaOptions mediaOptions = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(directory);
        var options = new DbContextOptionsBuilder<PartyGameDbContext>()
            .UseSqlite($"Data Source={Path.Combine(directory, "test.db")}")
            .Options;
        db = new PartyGameDbContext(options);
        await db.Database.MigrateAsync();
        mediaOptions = new MediaOptions { RootPath = relativeRoot };
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        var root = MediaStoragePathResolver.ResolveRootPath(relativeRoot);
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Theory]
    [InlineData(false, "image/jpeg")]
    [InlineData(true, "image/png")]
    public async Task ExistingLegacyImage_BackfillsActualMetadataAndIsIdempotent(bool png, string contentType)
    {
        var player = await AddLegacyPlayerAsync(png ? "legacy/profile.png" : "legacy/profile.jpg");
        var root = MediaStoragePathResolver.ResolveRootPath(relativeRoot);
        var path = MediaStoragePathResolver.ResolveStoragePath(root, player.ProfilePhotoStorageKey!);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = await ImageBytesAsync(png, 640, 480);
        await File.WriteAllBytesAsync(path, bytes);

        var first = await BackfillProfilePhotos.RunAsync(db, mediaOptions, NullLogger.Instance);
        var asset = await db.MediaAssets.SingleAsync();

        Assert.Equal(new ProfilePhotoBackfillResult(1, 1, 0, 0), first);
        Assert.Equal(MediaStoragePathResolver.ResolveRootPath(relativeRoot), root);
        Assert.Equal(contentType, asset.ContentType);
        Assert.Equal((640, 480), (asset.Width, asset.Height));
        Assert.Equal(bytes.Length, asset.ByteLength);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), asset.Sha256);
        Assert.Equal(player.Id, asset.PlayerId);
        Assert.Equal(player.RoomId, asset.RoomId);
        Assert.Equal(asset.Id, await db.Players.Where(candidate => candidate.Id == player.Id).Select(candidate => candidate.ProfilePhotoMediaAssetId).SingleAsync());

        var second = await BackfillProfilePhotos.RunAsync(db, mediaOptions, NullLogger.Instance);
        Assert.Equal(new ProfilePhotoBackfillResult(0, 0, 0, 0), second);
        Assert.Equal(1, await db.MediaAssets.CountAsync());
    }

    [Fact]
    public async Task MissingAndCorruptLegacyFiles_AreNotActivatedAndAreReported()
    {
        var missing = await AddLegacyPlayerAsync("legacy/missing.jpg");
        var corrupt = await AddLegacyPlayerAsync("legacy/corrupt.jpg");
        var root = MediaStoragePathResolver.ResolveRootPath(relativeRoot);
        var corruptPath = MediaStoragePathResolver.ResolveStoragePath(root, corrupt.ProfilePhotoStorageKey!);
        Directory.CreateDirectory(Path.GetDirectoryName(corruptPath)!);
        await File.WriteAllBytesAsync(corruptPath, "not an image"u8.ToArray());

        var result = await BackfillProfilePhotos.RunAsync(db, mediaOptions, NullLogger.Instance);

        Assert.Equal(new ProfilePhotoBackfillResult(2, 0, 1, 1), result);
        Assert.Null(await db.Players.Where(candidate => candidate.Id == missing.Id).Select(candidate => candidate.ProfilePhotoMediaAssetId).SingleAsync());
        Assert.Null(await db.Players.Where(candidate => candidate.Id == corrupt.Id).Select(candidate => candidate.ProfilePhotoMediaAssetId).SingleAsync());
        Assert.Empty(await db.MediaAssets.ToListAsync());
    }

    private async Task<Player> AddLegacyPlayerAsync(string storageKey)
    {
        var now = DateTimeOffset.UtcNow;
        var room = new GameRoom
        {
            Id = Guid.NewGuid(),
            Code = Guid.NewGuid().ToString("N")[..4].ToUpperInvariant(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            HostPlayerId = Guid.NewGuid(),
            Settings = new RoomSettings()
        };
        room.Settings.GameRoomId = room.Id;
        var player = new Player
        {
            Id = room.HostPlayerId,
            RoomId = room.Id,
            Room = room,
            Nickname = "Legacy",
            NormalizedNickname = $"LEGACY-{Guid.NewGuid():N}",
            IsHost = true,
            HasProfilePhoto = true,
            ProfilePhotoStorageKey = storageKey,
            ProfilePhotoContentType = "image/jpeg",
            JoinedAtUtc = now,
            LastSeenAtUtc = now,
            Session = new PlayerSession
            {
                ReconnectTokenHash = new string('a', 64),
                CreatedAtUtc = now,
                ExpiresAtUtc = now.AddDays(1)
            }
        };
        db.GameRooms.Add(room);
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player;
    }

    private static async Task<byte[]> ImageBytesAsync(bool png, int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, Color.MediumPurple);
        await using var stream = new MemoryStream();
        if (png) await image.SaveAsync(stream, new PngEncoder());
        else await image.SaveAsync(stream, new JpegEncoder());
        return stream.ToArray();
    }
}
