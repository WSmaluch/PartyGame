namespace PartyGame.Infrastructure.Media;

public static class MediaStoragePathResolver
{
    public static string ResolveRootPath(string configuredRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredRoot))
            throw new InvalidOperationException("The media storage root path is required.");

        return Path.GetFullPath(
            Path.IsPathRooted(configuredRoot)
                ? configuredRoot
                : Path.Combine(AppContext.BaseDirectory, configuredRoot));
    }

    public static string ResolveStoragePath(string rootPath, string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey))
            throw new InvalidOperationException("The media storage key is invalid.");

        var normalizedRoot = Path.GetFullPath(rootPath) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(rootPath, storageKey));
        if (!resolved.StartsWith(normalizedRoot, StringComparison.Ordinal))
            throw new InvalidOperationException("The media storage key is invalid.");

        return resolved;
    }
}
