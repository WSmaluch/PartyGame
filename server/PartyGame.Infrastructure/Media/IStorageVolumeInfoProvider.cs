namespace PartyGame.Infrastructure.Media;

public interface IStorageVolumeInfoProvider
{
    StorageVolumeInfo GetForPath(string rootPath);
}

public sealed record StorageVolumeInfo(long TotalBytes, long AvailableBytes);
