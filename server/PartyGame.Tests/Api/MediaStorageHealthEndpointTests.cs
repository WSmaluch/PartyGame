using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PartyGame.Domain.Content;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Tests.Infrastructure.Media;

namespace PartyGame.Tests.Api;

public sealed class MediaStorageHealthEndpointTests
{
    [Fact]
    public async Task GetStorageHealth_ReportsSafeMetricsForPersistedLocalMedia()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.Stage6B5.Health", Guid.NewGuid().ToString("N"));
        try
        {
            await using var harness = new PhotoAnswerTestHarness(
                directory,
                deleteOnDispose: false,
                configureServices: services =>
                {
                    services.RemoveAll<IStorageVolumeInfoProvider>();
                    services.AddSingleton<IStorageVolumeInfoProvider>(new StaticVolumeInfoProvider(1_000, 500));
                });
            var photoRoom = await harness.CreateRoomAsync();
            (await harness.UploadAsync(
                photoRoom,
                photoRoom.Players[0],
                await PhotoAnswerTestHarness.ImageAsync())).EnsureSuccessStatusCode();
            await AddProfileAsync(harness, photoRoom);
            await MakeExistingPackageNonConflictingAsync(harness);
            var drawingRoom = await harness.CreateRoomAsync(
                GameStage.CollectingDrawingAnswers,
                QuestionType.DrawingAnswer);
            (await harness.UploadDrawingAsync(
                drawingRoom,
                drawingRoom.Players[0],
                await PhotoAnswerTestHarness.DrawingAsync())).EnsureSuccessStatusCode();

            var ignoredTemporaryPath = Path.Combine(harness.Factory.MediaRootPath, ".tmp", "ignored.tmp");
            var ignoredUnknownKey = $"unknown/rooms/{Guid.NewGuid():N}/display.jpg";
            Directory.CreateDirectory(Path.GetDirectoryName(ignoredTemporaryPath)!);
            await File.WriteAllTextAsync(ignoredTemporaryPath, "temporary");
            var ignoredUnknownPath = MediaStoragePathResolver.ResolveStoragePath(
                harness.Factory.MediaRootPath,
                ignoredUnknownKey);
            Directory.CreateDirectory(Path.GetDirectoryName(ignoredUnknownPath)!);
            await File.WriteAllTextAsync(ignoredUnknownPath, "unknown");

            List<string> storageKeys;
            long mediaAssetCount;
            await using (var scope = harness.Factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
                mediaAssetCount = await db.MediaAssets.LongCountAsync();
                var assets = await db.MediaAssets.AsNoTracking()
                    .Select(asset => new MediaAssetKeys(asset.DisplayStorageKey, asset.ThumbnailStorageKey))
                    .ToListAsync();
                storageKeys = assets.SelectMany(asset => new[] { asset.Display, asset.Thumbnail }).ToList();
            }

            var response = await harness.Client.GetAsync("/health/storage");
            var body = await response.Content.ReadAsStringAsync();
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", root.GetProperty("status").GetString());
            Assert.True(root.GetProperty("probeSucceeded").GetBoolean());
            Assert.Equal(mediaAssetCount, root.GetProperty("mediaAssetCount").GetInt64());
            Assert.Equal(6, root.GetProperty("knownFinalFileCount").GetInt64());
            Assert.True(root.GetProperty("knownFinalFileBytes").GetInt64() > 0);
            Assert.True(root.GetProperty("totalBytes").GetInt64() > 0);
            Assert.True(root.GetProperty("availableBytes").GetInt64() >= 0);
            Assert.True(root.GetProperty("usedBytes").GetInt64() >= 0);
            Assert.InRange(root.GetProperty("availablePercent").GetDouble(), 0, 100);
            Assert.DoesNotContain(harness.Factory.MediaRootPath, body, StringComparison.Ordinal);
            Assert.DoesNotContain(ignoredUnknownKey, body, StringComparison.Ordinal);
            Assert.All(storageKeys, key => Assert.DoesNotContain(key, body, StringComparison.Ordinal));
            Assert.Empty(Directory.EnumerateFiles(
                Path.Combine(harness.Factory.MediaRootPath, ".diagnostics"),
                "*.probe",
                SearchOption.TopDirectoryOnly));

            var healthChecks = harness.Factory.Services.GetRequiredService<HealthCheckService>();
            var report = await healthChecks.CheckHealthAsync(registration => registration.Name == "media-storage");
            Assert.Equal(HealthStatus.Healthy, report.Status);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetStorageHealth_ReturnsControlledUnavailableResponseWhenProbeFails()
    {
        await using var harness = new PhotoAnswerTestHarness(
            configureServices: services =>
            {
                services.RemoveAll<IMediaStorageProbe>();
                services.AddSingleton<IMediaStorageProbe>(new FailingProbe());
            });

        var response = await harness.Client.GetAsync("/health/storage");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("status").GetString());
        Assert.False(json.RootElement.GetProperty("probeSucceeded").GetBoolean());
        Assert.DoesNotContain(harness.Factory.MediaRootPath, body, StringComparison.Ordinal);
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
        var player = await db.Players.SingleAsync(candidate => candidate.Id == room.Players[0].PlayerId);
        player.ProfilePhotoMediaAssetId = asset.Id;
        player.ProfilePhotoContentType = stored.ContentType;
        player.HasProfilePhoto = true;
        db.MediaAssets.Add(asset);
        await db.SaveChangesAsync();
    }

    private static async Task MakeExistingPackageNonConflictingAsync(PhotoAnswerTestHarness harness)
    {
        await using var scope = harness.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var package = await db.GamePackages.SingleAsync(candidate => candidate.Key.StartsWith("test-"));
        package.LogicalPackageId = package.Id;
        await db.SaveChangesAsync();
    }

    private sealed class FailingProbe : IMediaStorageProbe
    {
        public Task<bool> RunAsync(string rootPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class StaticVolumeInfoProvider(long totalBytes, long availableBytes) : IStorageVolumeInfoProvider
    {
        public StorageVolumeInfo GetForPath(string rootPath) => new(totalBytes, availableBytes);
    }

    private sealed record MediaAssetKeys(string Display, string Thumbnail);
}
