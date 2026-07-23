namespace PartyGame.Infrastructure.Media;

public interface IOrphanedGameMediaCleanupService
{
    Task<bool> CleanupAsync(Guid mediaAssetId, CancellationToken cancellationToken = default);

    Task<int> CleanupUnusedAsync(CancellationToken cancellationToken = default);
}
