using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class PhotoAnswerCleanupTests
{
    [Fact]
    public async Task DatabaseCommitFailure_CompensatesBothFinalFilesAndRows()
    {
        var interceptor = new ArmedCommitFailureInterceptor();
        await using var harness = new PhotoAnswerTestHarness(configureServices: services =>
        {
            services.AddSingleton(interceptor);
            services.AddDbContext<PartyGameDbContext>((provider, options) => options.AddInterceptors(provider.GetRequiredService<ArmedCommitFailureInterceptor>()));
        });
        var room = await harness.CreateRoomAsync(eligibleCount: 2);
        interceptor.Armed = true;

        var response = await harness.UploadAsync(room, room.Players[0], await PhotoAnswerTestHarness.ImageAsync());

        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("photo_answer_storage_failed", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var counts = await harness.CountsAsync(room.RoomCode);
        Assert.Equal((0, 0), (counts.Submissions, counts.Assets));
        Assert.Equal(0, harness.FinalJpegCount());
    }

    [Fact]
    public void StartupSweep_DeletesOnlyExpiredRecognizableTemporaryFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "PartyGame.TempSweep.Tests", Guid.NewGuid().ToString("N"));
        var temporary = Path.Combine(root, ".tmp");
        var final = Path.Combine(root, "rooms", "active", "display.jpg");
        Directory.CreateDirectory(temporary);
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        var old = Path.Combine(temporary, "old.tmp");
        var current = Path.Combine(temporary, "current.tmp");
        File.WriteAllText(old, "old");
        File.WriteAllText(current, "current");
        File.WriteAllText(final, "final");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-2));

        _ = new LocalMediaStorage(Options.Create(new MediaOptions { RootPath = root, TemporaryFileRetentionMinutes = 60 }));

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(final));
        Directory.Delete(root, recursive: true);
    }

    private sealed class ArmedCommitFailureInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context?.ChangeTracker.Entries<MediaAsset>().Any(entry => entry.State == EntityState.Added) == true)
            {
                throw new DbUpdateException("Injected photo-answer commit failure.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
