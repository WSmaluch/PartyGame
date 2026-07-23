namespace PartyGame.Infrastructure.Media;

public sealed class LocalStorageVolumeInfoProvider : IStorageVolumeInfoProvider
{
    public StorageVolumeInfo GetForPath(string rootPath)
    {
        var volumeRoot = Path.GetPathRoot(Path.GetFullPath(rootPath));
        if (string.IsNullOrWhiteSpace(volumeRoot))
            throw new IOException("The storage volume cannot be determined.");

        var drive = new DriveInfo(volumeRoot);
        return new StorageVolumeInfo(drive.TotalSize, drive.AvailableFreeSpace);
    }
}
