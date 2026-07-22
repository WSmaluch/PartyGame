namespace PartyGame.Infrastructure.Media;

public interface IProfilePhotoStorage
{
    Task<string> SaveAsync(string roomCode, Guid playerId, Stream content, string contentType, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}
