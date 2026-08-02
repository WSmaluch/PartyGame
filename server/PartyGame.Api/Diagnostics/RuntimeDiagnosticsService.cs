using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PartyGame.Api.Configuration;
using PartyGame.Api.Health;
using PartyGame.Api.Hubs;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Api.Diagnostics;

public interface IRuntimeDiagnosticsService
{
    Task<RuntimeDiagnosticsSummary> GetSummaryAsync(CancellationToken cancellationToken);
}

public sealed class RuntimeDiagnosticsService(
    IServiceScopeFactory scopeFactory,
    IOptions<MediaOptions> mediaOptions,
    IOptions<DeploymentOptions> deploymentOptions,
    IOptions<ReleaseRuntimeOptions> releaseRuntime,
    IOptions<DiagnosticsOptions> diagnosticsOptions,
    IRoomConnectionRegistry connections,
    BuildVersionInfo buildVersion,
    IHostApplicationLifetime applicationLifetime) : IRuntimeDiagnosticsService
{
    public async Task<RuntimeDiagnosticsSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var readiness = await RuntimeReadiness.CheckAsync(scopeFactory, mediaOptions, deploymentOptions, cancellationToken);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
        var schema = scope.ServiceProvider.GetRequiredService<DatabaseSchemaService>();
        var schemaStatus = await schema.GetStatusAsync(cancellationToken);
        var databaseSize = SafeFileSize(releaseRuntime.Value.DatabasePath, db.Database.GetDbConnection().DataSource);
        var media = GetMediaStats(mediaOptions.Value.RootPath);
        var lifecycle = GetLifecycleTimestamps(mediaOptions.Value.RootPath);
        var activeRooms = await db.GameRooms.CountAsync(room => room.Phase != PartyGame.Domain.Rooms.RoomPhase.Completed, cancellationToken);
        var process = Process.GetCurrentProcess();
        return new RuntimeDiagnosticsSummary(
            buildVersion.ToContract(schemaStatus.DatabaseSchemaVersion),
            DateTimeOffset.UtcNow,
            process.StartTime.ToUniversalTime(),
            Math.Max(0, (long)(DateTime.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds),
            readiness,
            new DatabaseDiagnostics(schemaStatus.DatabaseSchemaVersion, databaseSize, schemaStatus.DatabaseCompatibility),
            media,
            new ConnectionDiagnostics(activeRooms, connections.Count),
            lifecycle,
            new DeploymentDiagnostics(deploymentOptions.Value.Enabled ? "enabled" : "disabled", buildVersion.ApplicationVersion),
            new LogConfigurationDiagnostics(diagnosticsOptions.Value.IsJson ? "json" : "text", diagnosticsOptions.Value.LogFileSizeLimitMb, diagnosticsOptions.Value.LogRetainedFileCount, "configured"),
            applicationLifetime.ApplicationStopping.IsCancellationRequested ? "stopping" : "running");
    }

    private static long SafeFileSize(params string[] candidates)
    {
        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            try { if (File.Exists(candidate)) return new FileInfo(candidate).Length; }
            catch { }
        }
        return 0;
    }

    private static MediaDiagnostics GetMediaStats(string root)
    {
        try
        {
            var resolved = MediaStoragePathResolver.ResolveRootPath(root);
            if (!Directory.Exists(resolved)) return new MediaDiagnostics(0, 0, "unavailable");
            long count = 0, bytes = 0;
            foreach (var file in Directory.EnumerateFiles(resolved, "*", SearchOption.AllDirectories))
            {
                try { var info = new FileInfo(file); if (info.LinkTarget is null) { count++; bytes += info.Length; } }
                catch { }
            }
            return new MediaDiagnostics(count, bytes, "available");
        }
        catch { return new MediaDiagnostics(0, 0, "unavailable"); }
    }

    private static LifecycleDiagnostics GetLifecycleTimestamps(string mediaRoot)
    {
        try
        {
            var runtimeRoot = Directory.GetParent(MediaStoragePathResolver.ResolveRootPath(mediaRoot))?.FullName;
            if (runtimeRoot is null) return new LifecycleDiagnostics(null, null, null);
            return new LifecycleDiagnostics(NewestTimestamp(Path.Combine(runtimeRoot, "backups")), NewestTimestamp(Path.Combine(runtimeRoot, "restore")), NewestTimestamp(Path.Combine(runtimeRoot, "migrations")));
        }
        catch { return new LifecycleDiagnostics(null, null, null); }
    }

    private static DateTimeOffset? NewestTimestamp(string directory)
    {
        if (!Directory.Exists(directory)) return null;
        var newest = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Select(path => { try { return new FileInfo(path).LastWriteTimeUtc; } catch { return DateTime.MinValue; } })
            .DefaultIfEmpty(DateTime.MinValue).Max();
        return newest == DateTime.MinValue ? null : new DateTimeOffset(newest, TimeSpan.Zero);
    }
}

public sealed record RuntimeDiagnosticsSummary(VersionContract Version, DateTimeOffset CurrentUtc, DateTimeOffset ProcessStartedAtUtc, long UptimeSeconds, RuntimeReadinessResult Readiness, DatabaseDiagnostics Database, MediaDiagnostics Media, ConnectionDiagnostics Connections, LifecycleDiagnostics Lifecycle, DeploymentDiagnostics Deployment, LogConfigurationDiagnostics Logging, string ProcessStatus);
public sealed record DatabaseDiagnostics(string SchemaVersion, long SizeBytes, string Compatibility);
public sealed record MediaDiagnostics(long FileCount, long TotalSizeBytes, string Status);
public sealed record ConnectionDiagnostics(int ActiveRooms, int ActiveSignalRConnections);
public sealed record LifecycleDiagnostics(DateTimeOffset? LastSuccessfulBackupAtUtc, DateTimeOffset? LastRestoreAtUtc, DateTimeOffset? LastMigrationAtUtc);
public sealed record DeploymentDiagnostics(string Status, string Version);
public sealed record LogConfigurationDiagnostics(string Format, int FileSizeLimitMb, int RetainedFileCount, string DirectoryStatus);
