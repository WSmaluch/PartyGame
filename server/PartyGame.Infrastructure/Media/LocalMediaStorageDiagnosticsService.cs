using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Infrastructure.Media;

public sealed class LocalMediaStorageDiagnosticsService(
    IOptions<MediaOptions> options,
    ILocalMediaFileCatalog fileCatalog,
    IMediaStorageProbe probe,
    IStorageVolumeInfoProvider volumeInfoProvider,
    IServiceScopeFactory scopeFactory,
    IGameClock clock,
    ILogger<LocalMediaStorageDiagnosticsService> logger) : IMediaStorageDiagnosticsService
{
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private MediaStorageDiagnosticsResult? cached;

    public async Task<MediaStorageDiagnosticsResult> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var cacheDuration = TimeSpan.FromSeconds(options.Value.DiagnosticsCacheSeconds);
        var now = clock.UtcNow;
        if (cacheDuration > TimeSpan.Zero &&
            cached is { } current &&
            now - current.CheckedAtUtc <= cacheDuration)
            return current;

        await cacheLock.WaitAsync(cancellationToken);
        try
        {
            now = clock.UtcNow;
            if (cacheDuration > TimeSpan.Zero &&
                cached is { } lockedCurrent &&
                now - lockedCurrent.CheckedAtUtc <= cacheDuration)
                return lockedCurrent;

            var measured = await MeasureAsync(now, cancellationToken);
            cached = measured;
            return measured;
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private async Task<MediaStorageDiagnosticsResult> MeasureAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var mediaOptions = options.Value;
        if (!mediaOptions.DiagnosticsEnabled ||
            !string.Equals(mediaOptions.Provider, "LocalFileSystem", StringComparison.Ordinal))
        {
            return new MediaStorageDiagnosticsResult(
                MediaStorageDiagnosticStatus.NotSupported,
                mediaOptions.Provider,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                now,
                ["diagnostics_not_supported"]);
        }

        var warnings = new List<string>();
        var rootPath = MediaStoragePathResolver.ResolveRootPath(mediaOptions.RootPath);
        var probeSucceeded = await probe.RunAsync(rootPath, cancellationToken);
        if (!probeSucceeded)
            warnings.Add("storage_probe_failed");

        StorageVolumeInfo? volume = null;
        try
        {
            volume = volumeInfoProvider.GetForPath(rootPath);
            if (volume.TotalBytes <= 0 ||
                volume.AvailableBytes < 0 ||
                volume.AvailableBytes > volume.TotalBytes)
                throw new IOException("The storage volume returned invalid capacity values.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            volume = null;
            logger.LogWarning(
                "Media storage diagnostics could not read volume capacity; error type {ErrorType}",
                exception.GetType().Name);
            warnings.Add("volume_metrics_unavailable");
        }

        var mediaAssetCount = await CountMediaAssetsAsync(cancellationToken, warnings);
        var knownFiles = CountKnownFinalFiles(cancellationToken, warnings);
        var totalBytes = volume?.TotalBytes;
        var availableBytes = volume?.AvailableBytes;
        long? usedBytes = volume is null
            ? null
            : Math.Max(0, volume.TotalBytes - volume.AvailableBytes);
        double? availablePercent = volume is null
            ? null
            : Math.Clamp(volume.AvailableBytes * 100d / volume.TotalBytes, 0d, 100d);
        var status = ResolveStatus(
            probeSucceeded,
            availablePercent,
            mediaOptions.WarningFreePercent,
            mediaOptions.CriticalFreePercent,
            warnings);

        return new MediaStorageDiagnosticsResult(
            status,
            mediaOptions.Provider,
            probeSucceeded,
            totalBytes,
            availableBytes,
            usedBytes,
            availablePercent,
            mediaAssetCount,
            knownFiles.Count,
            knownFiles.Bytes,
            now,
            warnings);
    }

    private async Task<long?> CountMediaAssetsAsync(
        CancellationToken cancellationToken,
        ICollection<string> warnings)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            return await dbContext.MediaAssets.LongCountAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                "Media storage diagnostics could not count media assets; error type {ErrorType}",
                exception.GetType().Name);
            warnings.Add("media_asset_count_unavailable");
            return null;
        }
    }

    private (long Count, long Bytes) CountKnownFinalFiles(
        CancellationToken cancellationToken,
        ICollection<string> warnings)
    {
        try
        {
            long count = 0;
            long bytes = 0;
            foreach (var entry in fileCatalog.EnumerateFinalFiles(cancellationToken))
            {
                checked
                {
                    count++;
                    bytes += entry.ByteLength;
                }
            }

            return (count, bytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            logger.LogWarning(
                "Media storage diagnostics could not enumerate final media files; error type {ErrorType}",
                exception.GetType().Name);
            warnings.Add("known_file_metrics_unavailable");
            return (0, 0);
        }
    }

    private static MediaStorageDiagnosticStatus ResolveStatus(
        bool probeSucceeded,
        double? availablePercent,
        int warningFreePercent,
        int criticalFreePercent,
        ICollection<string> warnings)
    {
        if (!probeSucceeded || availablePercent is null)
            return MediaStorageDiagnosticStatus.Unhealthy;

        if (availablePercent <= criticalFreePercent)
        {
            warnings.Add("free_space_critical");
            return MediaStorageDiagnosticStatus.Unhealthy;
        }

        if (availablePercent <= warningFreePercent)
        {
            warnings.Add("free_space_warning");
            return MediaStorageDiagnosticStatus.Degraded;
        }

        return MediaStorageDiagnosticStatus.Healthy;
    }

}
