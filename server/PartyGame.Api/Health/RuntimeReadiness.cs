using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Api.Health;

public sealed record RuntimeReadinessResult(string Status, string Database, string MediaStorage);

public static class RuntimeReadiness
{
    public static async Task<RuntimeReadinessResult> CheckAsync(
        IServiceScopeFactory scopeFactory,
        IOptions<MediaOptions> mediaOptions,
        CancellationToken cancellationToken)
    {
        var databaseReady = false;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            databaseReady = await dbContext.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            databaseReady = false;
        }

        var mediaReady = false;
        try
        {
            var root = MediaStoragePathResolver.ResolveRootPath(mediaOptions.Value.RootPath);
            if (Directory.Exists(root))
            {
                _ = Directory.EnumerateFileSystemEntries(root).Take(1).ToArray();
                mediaReady = true;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            mediaReady = false;
        }

        return new RuntimeReadinessResult(
            databaseReady && mediaReady ? "ready" : "not-ready",
            databaseReady ? "ready" : "unavailable",
            mediaReady ? "ready" : "unavailable");
    }
}
