using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace PartyGame.Api.Diagnostics;

public interface ISupportBundleService
{
    Task<SupportBundleStatus> CreateAsync(string mode, CancellationToken cancellationToken);
    SupportBundleStatus? Get(Guid id);
    Stream? Open(Guid id, out string fileName);
}

public sealed class SupportBundleService(
    IRuntimeDiagnosticsService diagnostics,
    IOptions<DiagnosticsOptions> options,
    ILogger<SupportBundleService> logger) : ISupportBundleService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Dictionary<Guid, SupportBundleStatus> bundles = [];

    public async Task<SupportBundleStatus> CreateAsync(string mode, CancellationToken cancellationToken)
    {
        if (mode is not ("minimal" or "standard" or "extended")) throw new ArgumentException("Unsupported support bundle mode.", nameof(mode));
        if (!await Gate.WaitAsync(0, cancellationToken)) throw new SupportBundleBusyException();
        try
        {
            var root = Path.GetFullPath(options.Value.SupportBundleDirectory);
            Directory.CreateDirectory(root);
            var id = Guid.NewGuid();
            var timestamp = DateTimeOffset.UtcNow;
            var safeVersion = Regex.Replace((await diagnostics.GetSummaryAsync(cancellationToken)).Version.ApplicationVersion, "[^A-Za-z0-9._-]", "-");
            // Timestamp precision is intentionally human-friendly; append the bundle id so
            // independent operator requests in the same second never collide.
            var fileName = $"partygame-support-{timestamp:yyyyMMddTHHmmssZ}-{safeVersion}-{id.ToString("N")[..8]}.zip";
            var target = Path.Combine(root, fileName);
            var temporary = Path.Combine(root, $".{id:N}.tmp");
            var summary = await diagnostics.GetSummaryAsync(cancellationToken);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteText(archive, "SUPPORT_INFO.txt", $"PartyGame support bundle\nMode: {mode}\nCreated UTC: {timestamp:O}\nNo database, media, credentials, player data or raw request data is included.\n");
                WriteJson(archive, "diagnostics/summary.json", summary);
                WriteJson(archive, "version/version-contract.json", summary.Version);
                WriteJson(archive, "diagnostics/collection.json", new { mode, logsIncluded = 0, logsTruncated = false, databaseIncluded = false, mediaIncluded = false });
                WriteJson(archive, "configuration/safe-configuration.json", new { logFormat = summary.Logging.Format, retainedFileCount = summary.Logging.RetainedFileCount });
                WriteText(archive, "logs/README.txt", "Logs are collected by the deployment script after redaction; the API export never includes raw logs.\n");
                WriteText(archive, "database/OMITTED.txt", "Database data and SQLite sidecars are intentionally omitted.\n");
                WriteText(archive, "backup/OMITTED.txt", "Only backup metadata is eligible for an operator-generated bundle.\n");
                WriteText(archive, "network/OMITTED.txt", "Client addresses and topology are intentionally omitted.\n");
                WriteText(archive, "deployment/README.txt", "Deployment paths and secrets are intentionally omitted.\n");
                WriteJson(archive, "support-manifest.json", new
                {
                    supportBundleFormatVersion = BuildVersionInfo.SupportBundleFormatVersion,
                    createdAtUtc = timestamp,
                    applicationVersion = summary.Version.ApplicationVersion,
                    commitHash = summary.Version.CommitHash,
                    environment = summary.Version.Environment,
                    databaseSchemaVersion = summary.Database.SchemaVersion,
                    includedSections = new[] { "version", "diagnostics", "configuration", "logs", "deployment", "database", "backup", "network" },
                    omittedSections = new[] { "database-data", "sqlite-wal", "sqlite-shm", "media", "tokens", "player-data" },
                    redactionRulesVersion = "1",
                    sourceLogTimeRange = "none-api-export",
                    fileCount = 10,
                    totalUncompressedSize = 0,
                    checksums = new { },
                    diagnosticSummary = new { mode, logsTruncated = false }
                });
            }
            File.Move(temporary, target);
            var result = new SupportBundleStatus(id, "ready", fileName, timestamp, new FileInfo(target).Length, mode, null);
            bundles[id] = result;
            CleanupOldBundles(root, id);
            logger.LogInformation("Support bundle {BundleId} created with mode {Mode} and {SizeBytes} bytes", id, mode, result.SizeBytes);
            return result;
        }
        catch (Exception exception) when (exception is not SupportBundleBusyException)
        {
            logger.LogError(exception, "Support bundle generation failed");
            throw;
        }
        finally { Gate.Release(); }
    }

    public SupportBundleStatus? Get(Guid id) => bundles.GetValueOrDefault(id);

    public Stream? Open(Guid id, out string fileName)
    {
        fileName = string.Empty;
        if (!bundles.TryGetValue(id, out var status) || status.Status != "ready") return null;
        var path = Path.Combine(Path.GetFullPath(options.Value.SupportBundleDirectory), status.FileName);
        if (!File.Exists(path) || Path.GetFileName(path) != status.FileName) return null;
        fileName = status.FileName;
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    private static void WriteText(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(Redact(content));
    }

    private static void WriteJson(ZipArchive archive, string name, object value) =>
        WriteText(archive, name, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");

    private static string Redact(string value) => Regex.Replace(value, "(?i)(authorization|bearer|cookie|access[_-]?token|operator[_ -]?token|reconnect[_ -]?token)\\s*[:=]\\s*[^\\s,\\\"]+", "$1=[REDACTED]");

    private void CleanupOldBundles(string root, Guid latest)
    {
        foreach (var bundle in bundles.Values.Where(value => value.Id != latest).OrderByDescending(value => value.CreatedAtUtc).Skip(4).ToList())
        {
            try { File.Delete(Path.Combine(root, bundle.FileName)); bundles.Remove(bundle.Id); }
            catch (Exception exception) { logger.LogWarning(exception, "Could not remove expired support bundle {BundleId}", bundle.Id); }
        }
    }
}

public sealed record SupportBundleStatus(Guid Id, string Status, string FileName, DateTimeOffset CreatedAtUtc, long SizeBytes, string Mode, string? ErrorCode);
public sealed class SupportBundleBusyException : Exception { }
