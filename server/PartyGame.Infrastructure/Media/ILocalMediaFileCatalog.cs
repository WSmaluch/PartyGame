namespace PartyGame.Infrastructure.Media;

public interface ILocalMediaFileCatalog
{
    IEnumerable<LocalMediaFileEntry> EnumerateFinalFiles(CancellationToken cancellationToken = default);

    Task<LocalMediaFileEntry?> GetFinalFileAsync(
        string storageKey,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default);
}

public sealed record LocalMediaFileEntry(
    string StorageKey,
    DateTimeOffset LastWriteTimeUtc,
    long ByteLength = 0);
