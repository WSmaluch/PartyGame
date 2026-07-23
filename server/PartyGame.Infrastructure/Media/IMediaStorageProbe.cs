namespace PartyGame.Infrastructure.Media;

public interface IMediaStorageProbe
{
    Task<bool> RunAsync(string rootPath, CancellationToken cancellationToken = default);
}
