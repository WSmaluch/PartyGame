namespace PartyGame.Infrastructure.Media;

public interface IProfilePhotoCleanupService
{
    Task<bool> CleanupAsync(Guid mediaAssetId, CancellationToken cancellationToken = default);

    Task<int> CleanupUnusedAsync(CancellationToken cancellationToken = default);
}
