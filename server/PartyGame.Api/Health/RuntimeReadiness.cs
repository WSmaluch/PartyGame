using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PartyGame.Api.Configuration;
using PartyGame.Api.Diagnostics;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Api.Health;

public sealed record RuntimeReadinessResult(
    string Status,
    string Database,
    string Schema,
    string MediaStorage,
    string DataOperation,
    string Display,
    string Admin,
    string Player);

public static class RuntimeReadiness
{
    public static async Task<RuntimeReadinessResult> CheckAsync(
        IServiceScopeFactory scopeFactory,
        IOptions<MediaOptions> mediaOptions,
        IOptions<DeploymentOptions> deploymentOptions,
        CancellationToken cancellationToken)
    {
        var databaseReady = false;
        var schemaReady = false;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            databaseReady = await dbContext.Database.CanConnectAsync(cancellationToken);
            if (databaseReady)
            {
                var schema = scope.ServiceProvider.GetRequiredService<DatabaseSchemaService>();
                var status = await schema.GetStatusAsync(cancellationToken);
                schemaReady = status.DatabaseCompatibility == "compatible" && !status.MigrationRequired;
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            databaseReady = false;
        }

        var mediaReady = false;
        var dataOperationActive = false;
        try
        {
            var root = MediaStoragePathResolver.ResolveRootPath(mediaOptions.Value.RootPath);
            if (Directory.Exists(root))
            {
                _ = Directory.EnumerateFileSystemEntries(root).Take(1).ToArray();
                mediaReady = true;
                var runtime = Directory.GetParent(root)?.FullName;
                dataOperationActive = runtime is not null && Directory.Exists(Path.Combine(runtime, "operations", "data-operation.lock"));
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            mediaReady = false;
        }

        var displayReady = IsStaticRootReady(deploymentOptions.Value.Enabled, deploymentOptions.Value.DisplayRoot);
        var adminReady = IsStaticRootReady(deploymentOptions.Value.Enabled, deploymentOptions.Value.AdminRoot);
        var playerReady = IsStaticRootReady(deploymentOptions.Value.Enabled, deploymentOptions.Value.PlayerRoot);

        return new RuntimeReadinessResult(
            databaseReady && schemaReady && mediaReady && !dataOperationActive && displayReady && adminReady && playerReady ? "ready" : "not-ready",
            databaseReady ? "ready" : "unavailable",
            schemaReady ? "compatible" : "migration-required-or-incompatible",
            mediaReady ? "ready" : "unavailable",
            dataOperationActive ? "active" : "idle",
            displayReady ? "ready" : "unavailable",
            adminReady ? "ready" : "unavailable",
            playerReady ? "ready" : "unavailable");
    }

    private static bool IsStaticRootReady(bool deploymentEnabled, string root)
    {
        if (!deploymentEnabled) return true;
        try
        {
            return Directory.Exists(root) && File.Exists(Path.Combine(root, "index.html"));
        }
        catch { return false; }
    }
}
