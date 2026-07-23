using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Infrastructure.Media;

public sealed class OrphanedGameMediaCleanupService(
    PartyGameDbContext dbContext,
    IMediaStorage mediaStorage,
    IOptions<MediaOptions> options,
    ILogger<OrphanedGameMediaCleanupService> logger) : IOrphanedGameMediaCleanupService
{
    private const int MaximumBatchSize = 100;

    public async Task<bool> CleanupAsync(Guid mediaAssetId, CancellationToken cancellationToken = default)
    {
        if (!await DeleteVariantAsync(mediaAssetId, "display", cancellationToken))
            return false;

        if (!await DeleteVariantAsync(mediaAssetId, "thumbnail", cancellationToken))
            return false;

        var asset = await FindEligibleAssetAsync(mediaAssetId, cancellationToken);
        if (asset is null)
            return false;

        dbContext.MediaAssets.Remove(asset);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Removed orphaned game media asset {MediaAssetId} of kind {MediaKind}",
                asset.Id,
                asset.MediaKind);
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
                "Game media cleanup could not remove database record for media asset {MediaAssetId} of kind {MediaKind}; error type {ErrorType}",
                asset.Id,
                asset.MediaKind,
                exception.GetType().Name);
            return false;
        }
    }

    public async Task<int> CleanupUnusedAsync(CancellationToken cancellationToken = default)
    {
        var batchSize = Math.Clamp(options.Value.OrphanedGameMediaCleanupBatchSize, 1, MaximumBatchSize);
        // SQLite cannot translate an EF OrderBy over DateTimeOffset. Keep the candidate
        // query ordered and bounded in SQL, then re-check every asset before deletion.
        var assetIds = await dbContext.Database.SqlQuery<Guid>($"""
            SELECT asset."Id" AS "Value"
            FROM "MediaAssets" AS asset
            WHERE (asset."MediaKind" = {(int)MediaKind.PhotoAnswer}
                   AND NOT EXISTS (
                       SELECT 1
                       FROM "PhotoAnswerSubmissions" AS submission
                       WHERE submission."MediaAssetId" = asset."Id"))
               OR (asset."MediaKind" = {(int)MediaKind.DrawingAnswer}
                   AND NOT EXISTS (
                       SELECT 1
                       FROM "DrawingAnswerSubmissions" AS submission
                       WHERE submission."MediaAssetId" = asset."Id"))
            ORDER BY asset."CreatedAtUtc", asset."Id"
            LIMIT {batchSize}
            """)
            .ToListAsync(cancellationToken);

        var removed = 0;
        foreach (var assetId in assetIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await CleanupAsync(assetId, cancellationToken))
                    removed++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    "Game media cleanup skipped media asset {MediaAssetId}; error type {ErrorType}",
                    assetId,
                    exception.GetType().Name);
            }
        }

        return removed;
    }

    private Task<MediaAsset?> FindEligibleAssetAsync(Guid mediaAssetId, CancellationToken cancellationToken) =>
        dbContext.MediaAssets.AsNoTracking().SingleOrDefaultAsync(
            asset =>
                asset.Id == mediaAssetId &&
                ((asset.MediaKind == MediaKind.PhotoAnswer &&
                  !dbContext.PhotoAnswerSubmissions.Any(submission => submission.MediaAssetId == mediaAssetId)) ||
                 (asset.MediaKind == MediaKind.DrawingAnswer &&
                  !dbContext.DrawingAnswerSubmissions.Any(submission => submission.MediaAssetId == mediaAssetId))),
            cancellationToken);

    private async Task<bool> DeleteVariantAsync(Guid mediaAssetId, string variant, CancellationToken cancellationToken)
    {
        var asset = await FindEligibleAssetAsync(mediaAssetId, cancellationToken);
        if (asset is null)
            return false;

        var storageKey = variant == "display" ? asset.DisplayStorageKey : asset.ThumbnailStorageKey;
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
                "Game media cleanup could not remove {Variant} variant for media asset {MediaAssetId} of kind {MediaKind}; error type {ErrorType}",
                variant,
                asset.Id,
                asset.MediaKind,
                exception.GetType().Name);
            return false;
        }
    }
}
