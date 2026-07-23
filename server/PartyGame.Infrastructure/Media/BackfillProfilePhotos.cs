using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;
using SixLabors.ImageSharp;

public sealed record ProfilePhotoBackfillResult(int Candidates, int Backfilled, int Skipped, int Errors);

public static class BackfillProfilePhotos
{
    public static async Task<ProfilePhotoBackfillResult> RunAsync(IServiceProvider provider, CancellationToken cancellationToken = default)
    {
        var db = provider.GetRequiredService<PartyGameDbContext>();
        var options = provider.GetRequiredService<IOptions<MediaOptions>>().Value;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("BackfillProfilePhotos");
        return await RunAsync(db, options, logger, cancellationToken);
    }

    public static async Task<ProfilePhotoBackfillResult> RunAsync(
        PartyGameDbContext db,
        MediaOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var rootPath = MediaStoragePathResolver.ResolveRootPath(options.RootPath);
        var players = await db.Players
            .Where(player => player.ProfilePhotoStorageKey != null && player.ProfilePhotoMediaAssetId == null)
            .ToListAsync(cancellationToken);
        var backfilled = 0;
        var skipped = 0;
        var errors = 0;

        foreach (var player in players)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var filePath = MediaStoragePathResolver.ResolveStoragePath(rootPath, player.ProfilePhotoStorageKey!);
                if (!File.Exists(filePath))
                {
                    skipped++;
                    logger.LogWarning("Skipping legacy profile photo for player {PlayerId}: file is missing.", player.Id);
                    continue;
                }

                await using var input = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                using var image = await Image.LoadAsync(input, cancellationToken);
                var contentType = image.Metadata.DecodedImageFormat?.DefaultMimeType;
                if (contentType is not ("image/jpeg" or "image/png"))
                {
                    skipped++;
                    logger.LogWarning("Skipping legacy profile photo for player {PlayerId}: unsupported image format.", player.Id);
                    continue;
                }

                var byteLength = input.Length;
                input.Position = 0;
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(input, cancellationToken));
                var asset = new MediaAsset
                {
                    Id = Guid.NewGuid(),
                    MediaKind = MediaKind.ProfilePhoto,
                    StorageProvider = "LocalFileSystem",
                    RoomId = player.RoomId,
                    PlayerId = player.Id,
                    DisplayStorageKey = player.ProfilePhotoStorageKey!,
                    ThumbnailStorageKey = player.ProfilePhotoStorageKey!,
                    ContentType = contentType,
                    Width = image.Width,
                    Height = image.Height,
                    ByteLength = byteLength,
                    Sha256 = hash,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };

                db.MediaAssets.Add(asset);
                player.ProfilePhotoMediaAssetId = asset.Id;
                backfilled++;
            }
            catch (UnknownImageFormatException exception)
            {
                errors++;
                logger.LogWarning(exception, "Skipping legacy profile photo for player {PlayerId}: invalid image.", player.Id);
            }
            catch (InvalidImageContentException exception)
            {
                errors++;
                logger.LogWarning(exception, "Skipping legacy profile photo for player {PlayerId}: invalid image.", player.Id);
            }
            catch (InvalidOperationException exception)
            {
                errors++;
                logger.LogWarning(exception, "Skipping legacy profile photo for player {PlayerId}: invalid storage key.", player.Id);
            }
            catch (IOException exception)
            {
                errors++;
                logger.LogWarning(exception, "Skipping legacy profile photo for player {PlayerId}: file cannot be read.", player.Id);
            }
        }

        if (backfilled > 0)
            await db.SaveChangesAsync(cancellationToken);

        var result = new ProfilePhotoBackfillResult(players.Count, backfilled, skipped, errors);
        logger.LogInformation(
            "Legacy profile photo backfill completed: {Candidates} candidates, {Backfilled} backfilled, {Skipped} skipped, {Errors} errors.",
            result.Candidates,
            result.Backfilled,
            result.Skipped,
            result.Errors);
        return result;
    }
}
