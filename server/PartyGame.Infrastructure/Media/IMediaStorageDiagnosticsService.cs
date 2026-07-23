namespace PartyGame.Infrastructure.Media;

public interface IMediaStorageDiagnosticsService
{
    Task<MediaStorageDiagnosticsResult> GetAsync(
        CancellationToken cancellationToken = default);
}

public enum MediaStorageDiagnosticStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    NotSupported
}

public sealed record MediaStorageDiagnosticsResult(
    MediaStorageDiagnosticStatus Status,
    string Provider,
    bool ProbeSucceeded,
    long? TotalBytes,
    long? AvailableBytes,
    long? UsedBytes,
    double? AvailablePercent,
    long? MediaAssetCount,
    long? KnownFinalFileCount,
    long? KnownFinalFileBytes,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<string> Warnings);
