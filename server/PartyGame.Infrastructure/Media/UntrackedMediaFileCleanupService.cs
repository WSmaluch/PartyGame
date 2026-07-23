using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Infrastructure.Media;

public sealed class UntrackedMediaFileCleanupService(
    PartyGameDbContext dbContext,
    ILocalMediaFileCatalog fileCatalog,
    IOptions<MediaOptions> options,
    IGameClock clock,
    ILogger<UntrackedMediaFileCleanupService> logger) : IUntrackedMediaFileCleanupService
{
    private const int MaximumBatchSize = 100;

    public async Task<UntrackedMediaFileCleanupResult> CleanupAsync(
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.UntrackedFileCleanupEnabled)
            return new UntrackedMediaFileCleanupResult(0, 0, 0, 0, 0, 0, 0);

        var batchSize = Math.Clamp(
            options.Value.UntrackedFileCleanupBatchSize,
            1,
            MaximumBatchSize);
        var gracePeriod = TimeSpan.FromMinutes(
            Math.Max(1, options.Value.UntrackedFileCleanupGracePeriodMinutes));
        var cutoff = clock.UtcNow - gracePeriod;
        var scanned = 0;
        var candidates = 0;
        var deleted = 0;
        var skippedReferenced = 0;
        var skippedTooYoung = 0;
        var missing = 0;
        var failed = 0;

        foreach (var entry in fileCatalog.EnumerateFinalFiles(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            scanned++;

            if (entry.LastWriteTimeUtc >= cutoff)
            {
                skippedTooYoung++;
                continue;
            }

            if (await IsReferencedAsync(entry.StorageKey, cancellationToken))
            {
                skippedReferenced++;
                continue;
            }

            candidates++;
            try
            {
                var current = await fileCatalog.GetFinalFileAsync(
                    entry.StorageKey,
                    cancellationToken);
                if (current is null)
                {
                    missing++;
                }
                else if (current.LastWriteTimeUtc >= cutoff)
                {
                    skippedTooYoung++;
                }
                else if (await IsReferencedAsync(entry.StorageKey, cancellationToken))
                {
                    skippedReferenced++;
                }
                else if (await fileCatalog.DeleteAsync(entry.StorageKey, cancellationToken))
                {
                    deleted++;
                }
                else
                {
                    missing++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failed++;
                logger.LogWarning(
                    "Untracked media file cleanup could not remove storage key {StorageKey}; error type {ErrorType}",
                    entry.StorageKey,
                    exception.GetType().Name);
            }

            if (candidates >= batchSize)
                break;
        }

        return new UntrackedMediaFileCleanupResult(
            scanned,
            candidates,
            deleted,
            skippedReferenced,
            skippedTooYoung,
            missing,
            failed);
    }

    private Task<bool> IsReferencedAsync(
        string storageKey,
        CancellationToken cancellationToken) =>
        dbContext.MediaAssets.AsNoTracking().AnyAsync(
            asset =>
                asset.DisplayStorageKey == storageKey ||
                asset.ThumbnailStorageKey == storageKey,
            cancellationToken);
}
