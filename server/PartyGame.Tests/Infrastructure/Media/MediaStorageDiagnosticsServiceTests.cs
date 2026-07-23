using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Media;
using PartyGame.Tests.Api;

namespace PartyGame.Tests.Infrastructure.Media;

public sealed class MediaStorageDiagnosticsServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsHealthyCapacityAndKnownFileMetrics()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var catalog = new FakeCatalog(
        [
            Entry("profile/rooms/a/players/b/c/display.jpg", 12),
            Entry("photo-answer/rooms/a/questions/b/c/thumbnail.jpg", 8)
        ]);
        var probe = new FakeProbe(succeeds: true);
        var service = CreateService(harness, catalog, probe, new FakeVolumeInfoProvider(1_000, 500));

        var result = await service.GetAsync();

        Assert.Equal(MediaStorageDiagnosticStatus.Healthy, result.Status);
        Assert.True(result.ProbeSucceeded);
        Assert.Equal(1_000, result.TotalBytes);
        Assert.Equal(500, result.AvailableBytes);
        Assert.Equal(500, result.UsedBytes);
        Assert.Equal(50, result.AvailablePercent);
        Assert.Equal(0, result.MediaAssetCount);
        Assert.Equal(2, result.KnownFinalFileCount);
        Assert.Equal(20, result.KnownFinalFileBytes);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData(100, 10, MediaStorageDiagnosticStatus.Degraded, "free_space_warning")]
    [InlineData(100, 5, MediaStorageDiagnosticStatus.Unhealthy, "free_space_critical")]
    public async Task GetAsync_AppliesConfiguredFreeSpaceThresholds(
        long totalBytes,
        long availableBytes,
        MediaStorageDiagnosticStatus expectedStatus,
        string expectedWarning)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var service = CreateService(
            harness,
            new FakeCatalog([]),
            new FakeProbe(succeeds: true),
            new FakeVolumeInfoProvider(totalBytes, availableBytes));

        var result = await service.GetAsync();

        Assert.Equal(expectedStatus, result.Status);
        Assert.Contains(expectedWarning, result.Warnings);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("read")]
    public async Task GetAsync_ReturnsUnhealthyWhenProbeFails(string failureKind)
    {
        await using var harness = new PhotoAnswerTestHarness();
        var probe = new FakeProbe(succeeds: false, failureKind);
        var service = CreateService(
            harness,
            new FakeCatalog([]),
            probe,
            new FakeVolumeInfoProvider(100, 80));

        var result = await service.GetAsync();

        Assert.Equal(MediaStorageDiagnosticStatus.Unhealthy, result.Status);
        Assert.False(result.ProbeSucceeded);
        Assert.Contains("storage_probe_failed", result.Warnings);
        Assert.Equal(failureKind, probe.FailureKind);
        Assert.Equal(1, probe.Calls);
    }

    [Fact]
    public async Task GetAsync_CachesMeasurementUntilTheConfiguredDurationExpires()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var clock = new MutableClock(DateTimeOffset.Parse("2026-07-23T10:00:00Z"));
        var catalog = new FakeCatalog([Entry("profile/rooms/a/players/b/c/display.jpg", 1)]);
        var probe = new FakeProbe(succeeds: true);
        var volume = new FakeVolumeInfoProvider(100, 80);
        var service = CreateService(harness, catalog, probe, volume, clock, cacheSeconds: 30);

        var first = await service.GetAsync();
        var second = await service.GetAsync();
        clock.UtcNow = clock.UtcNow.AddSeconds(31);
        var third = await service.GetAsync();

        Assert.Same(first, second);
        Assert.NotSame(first, third);
        Assert.Equal(2, probe.Calls);
        Assert.Equal(2, volume.Calls);
        Assert.Equal(2, catalog.Enumerations);
    }

    [Fact]
    public async Task GetAsync_CacheSecondsZeroDisablesCaching()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var catalog = new FakeCatalog([]);
        var probe = new FakeProbe(succeeds: true);
        var volume = new FakeVolumeInfoProvider(100, 80);
        var service = CreateService(harness, catalog, probe, volume, cacheSeconds: 0);

        var first = await service.GetAsync();
        var second = await service.GetAsync();

        Assert.NotSame(first, second);
        Assert.Equal(2, probe.Calls);
        Assert.Equal(2, volume.Calls);
        Assert.Equal(2, catalog.Enumerations);
    }

    [Fact]
    public async Task GetAsync_ConcurrentRequestsShareOneCachedMeasurement()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var catalog = new FakeCatalog([]);
        var probe = new BlockingProbe();
        var volume = new FakeVolumeInfoProvider(100, 80);
        var service = CreateService(harness, catalog, probe, volume, cacheSeconds: 30);

        var firstTask = service.GetAsync();
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondTask = service.GetAsync();
        probe.Release.TrySetResult();
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Same(results[0], results[1]);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(1, volume.Calls);
        Assert.Equal(1, catalog.Enumerations);
    }

    [Fact]
    public async Task GetAsync_RejectsCapacityWhereAvailableExceedsTotal()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var service = CreateService(
            harness,
            new FakeCatalog([]),
            new FakeProbe(succeeds: true),
            new FakeVolumeInfoProvider(100, 101));

        var result = await service.GetAsync();

        Assert.Equal(MediaStorageDiagnosticStatus.Unhealthy, result.Status);
        Assert.Null(result.TotalBytes);
        Assert.Null(result.AvailableBytes);
        Assert.Null(result.UsedBytes);
        Assert.Null(result.AvailablePercent);
        Assert.Contains("volume_metrics_unavailable", result.Warnings);
    }

    [Fact]
    public async Task GetAsync_ReturnsNotSupportedWithoutRunningALocalProbeForAnotherProvider()
    {
        await using var harness = new PhotoAnswerTestHarness();
        var probe = new FakeProbe(succeeds: true);
        var service = CreateService(
            harness,
            new FakeCatalog([]),
            probe,
            new FakeVolumeInfoProvider(100, 80),
            provider: "CloudObjectStorage");

        var result = await service.GetAsync();

        Assert.Equal(MediaStorageDiagnosticStatus.NotSupported, result.Status);
        Assert.False(result.ProbeSucceeded);
        Assert.Contains("diagnostics_not_supported", result.Warnings);
        Assert.Equal(0, probe.Calls);
    }

    private static LocalMediaStorageDiagnosticsService CreateService(
        PhotoAnswerTestHarness harness,
        FakeCatalog catalog,
        IMediaStorageProbe probe,
        FakeVolumeInfoProvider volume,
        MutableClock? clock = null,
        int cacheSeconds = 0,
        string provider = "LocalFileSystem") =>
        new(
            Options.Create(new MediaOptions
            {
                Provider = provider,
                RootPath = harness.Factory.MediaRootPath,
                DiagnosticsEnabled = true,
                DiagnosticsCacheSeconds = cacheSeconds,
                WarningFreePercent = 10,
                CriticalFreePercent = 5
            }),
            catalog,
            probe,
            volume,
            harness.Factory.Services.GetRequiredService<IServiceScopeFactory>(),
            clock ?? new MutableClock(DateTimeOffset.Parse("2026-07-23T10:00:00Z")),
            NullLogger<LocalMediaStorageDiagnosticsService>.Instance);

    private static LocalMediaFileEntry Entry(string key, long bytes) =>
        new(key, DateTimeOffset.Parse("2026-07-23T10:00:00Z"), bytes);

    private sealed class FakeCatalog(IReadOnlyList<LocalMediaFileEntry> entries) : ILocalMediaFileCatalog
    {
        public int Enumerations { get; private set; }

        public IEnumerable<LocalMediaFileEntry> EnumerateFinalFiles(CancellationToken cancellationToken = default)
        {
            Enumerations++;
            return entries;
        }

        public Task<LocalMediaFileEntry?> GetFinalFileAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<LocalMediaFileEntry?>(entries.SingleOrDefault(entry => entry.StorageKey == storageKey));

        public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeProbe(bool succeeds, string? failureKind = null) : IMediaStorageProbe
    {
        public int Calls { get; private set; }
        public string? FailureKind { get; } = failureKind;

        public Task<bool> RunAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(succeeds);
        }
    }

    private sealed class BlockingProbe : IMediaStorageProbe
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }

        public async Task<bool> RunAsync(
            string rootPath,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return true;
        }
    }

    private sealed class FakeVolumeInfoProvider(long totalBytes, long availableBytes) : IStorageVolumeInfoProvider
    {
        public int Calls { get; private set; }

        public StorageVolumeInfo GetForPath(string rootPath)
        {
            Calls++;
            return new StorageVolumeInfo(totalBytes, availableBytes);
        }
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
