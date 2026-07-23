using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Infrastructure.Media;

public sealed class ProfilePhotoCleanupService(
    PartyGameDbContext dbContext,
    IMediaStorage mediaStorage,
    IOptions<MediaOptions> options,
    ILogger<ProfilePhotoCleanupService> logger) : IProfilePhotoCleanupService
{
    private const int MaximumBatchSize = 100;

    public async Task<bool> CleanupAsync(Guid mediaAssetId, CancellationToken cancellationToken = default)
    {
        var asset = await dbContext.MediaAssets.SingleOrDefaultAsync(
            candidate => candidate.Id == mediaAssetId && candidate.MediaKind == MediaKind.ProfilePhoto,
            cancellationToken);
        if (asset is null || !await IsEligibleForCleanupAsync(mediaAssetId, cancellationToken))
            return false;

        if (!await DeleteVariantAsync(asset.Id, "display", asset.DisplayStorageKey, cancellationToken))
            return false;
        if (!await DeleteVariantAsync(asset.Id, "thumbnail", asset.ThumbnailStorageKey, cancellationToken))
            return false;

        if (!await IsEligibleForCleanupAsync(mediaAssetId, cancellationToken))
            return false;

        dbContext.MediaAssets.Remove(asset);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Removed unused profile photo media asset {MediaAssetId}", mediaAssetId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            dbContext.Entry(asset).State = EntityState.Unchanged;
            logger.LogWarning(
                "Profile photo cleanup could not remove database record for media asset {MediaAssetId}; error type {ErrorType}",
                mediaAssetId,
                exception.GetType().Name);
            return false;
        }
    }

    public async Task<int> CleanupUnusedAsync(CancellationToken cancellationToken = default)
    {
        var batchSize = Math.Clamp(options.Value.ProfilePhotoCleanupBatchSize, 1, MaximumBatchSize);
        // SQLite cannot translate an EF OrderBy over DateTimeOffset. Keep the ordering
        // and limit in SQL so startup never has to materialize every orphan candidate.
        var assetIds = await dbContext.Database.SqlQuery<Guid>($"""
            SELECT asset."Id" AS "Value"
            FROM "MediaAssets" AS asset
            WHERE asset."MediaKind" = {(int)MediaKind.ProfilePhoto}
              AND NOT EXISTS (
                  SELECT 1
                  FROM "Players" AS player
                  WHERE player."ProfilePhotoMediaAssetId" = asset."Id")
            ORDER BY asset."CreatedAtUtc", asset."Id"
            LIMIT {batchSize}
            """)
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var assetId in assetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await CleanupAsync(assetId, cancellationToken))
                removed++;
        }

        return removed;
    }

    private Task<bool> IsEligibleForCleanupAsync(Guid mediaAssetId, CancellationToken cancellationToken) =>
        dbContext.MediaAssets.AnyAsync(
            asset =>
                asset.Id == mediaAssetId &&
                asset.MediaKind == MediaKind.ProfilePhoto &&
                !dbContext.Players.Any(player => player.ProfilePhotoMediaAssetId == mediaAssetId),
            cancellationToken);

    private async Task<bool> DeleteVariantAsync(Guid mediaAssetId, string variant, string storageKey, CancellationToken cancellationToken)
    {
        if (!await IsEligibleForCleanupAsync(mediaAssetId, cancellationToken))
            return false;

        try
        {
            await mediaStorage.DeleteAsync(storageKey, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Profile photo cleanup could not remove {Variant} variant for media asset {MediaAssetId}; error type {ErrorType}",
                variant,
                mediaAssetId,
                exception.GetType().Name);
            return false;
        }
    }
}
