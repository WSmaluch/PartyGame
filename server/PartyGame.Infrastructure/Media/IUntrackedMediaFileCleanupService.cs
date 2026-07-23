namespace PartyGame.Infrastructure.Media;

public interface IUntrackedMediaFileCleanupService
{
    Task<UntrackedMediaFileCleanupResult> CleanupAsync(
        CancellationToken cancellationToken = default);
}

public sealed record UntrackedMediaFileCleanupResult(
    int Scanned,
    int Candidates,
    int Deleted,
    int SkippedReferenced,
    int SkippedTooYoung,
    int Missing,
    int Failed);
