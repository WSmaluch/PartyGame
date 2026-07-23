using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PartyGame.Infrastructure.Media;

public sealed class LocalMediaFileCatalog(
    IOptions<MediaOptions> options,
    ILogger<LocalMediaFileCatalog> logger) : ILocalMediaFileCatalog
{
    private static readonly string[] VariantNames = ["display", "thumbnail"];
    private readonly string rootPath = MediaStoragePathResolver.ResolveRootPath(options.Value.RootPath);

    public IEnumerable<LocalMediaFileEntry> EnumerateFinalFiles(
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeRoot())
            yield break;

        foreach (var entry in EnumerateAnswerFiles("drawing-answer", ".png", cancellationToken))
            yield return entry;

        foreach (var entry in EnumerateAnswerFiles("photo-answer", ".jpg", cancellationToken))
            yield return entry;

        foreach (var entry in EnumerateProfileFiles(cancellationToken))
            yield return entry;
    }

    public Task<LocalMediaFileEntry?> GetFinalFileAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryParseStorageKey(storageKey, out var normalizedKey))
            throw new InvalidOperationException("The local media storage key is not a recognized final variant.");

        return Task.FromResult(ReadEntry(normalizedKey));
    }

    public Task<bool> DeleteAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryParseStorageKey(storageKey, out var normalizedKey))
            throw new InvalidOperationException("The local media storage key is not a recognized final variant.");

        var path = MediaStoragePathResolver.ResolveStoragePath(rootPath, normalizedKey);
        if (!IsSafeExistingPath(path, normalizedKey))
            return Task.FromResult(false);

        File.Delete(path);
        return Task.FromResult(true);
    }

    private IEnumerable<LocalMediaFileEntry> EnumerateProfileFiles(CancellationToken cancellationToken)
    {
        const string tree = "profile";
        var roomsPath = SafeKnownDirectory(Path.Combine(rootPath, tree, "rooms"), $"{tree}/rooms");
        if (roomsPath is null)
            yield break;

        foreach (var roomPath in SafeGuidDirectories(roomsPath, $"{tree}/rooms", cancellationToken))
        {
            var room = Path.GetFileName(roomPath);
            var playersPath = SafeKnownDirectory(
                Path.Combine(roomPath, "players"),
                $"{tree}/rooms/{room}/players");
            if (playersPath is null)
                continue;

            foreach (var playerPath in SafeGuidDirectories(
                         playersPath,
                         $"{tree}/rooms/{room}/players",
                         cancellationToken))
            {
                var player = Path.GetFileName(playerPath);
                foreach (var assetPath in SafeGuidDirectories(
                             playerPath,
                             $"{tree}/rooms/{room}/players/{player}",
                             cancellationToken))
                {
                    var asset = Path.GetFileName(assetPath);
                    foreach (var entry in ReadVariants(
                                 assetPath,
                                 $"{tree}/rooms/{room}/players/{player}/{asset}",
                                 ".jpg",
                                 cancellationToken))
                    {
                        yield return entry;
                    }
                }
            }
        }
    }

    private IEnumerable<LocalMediaFileEntry> EnumerateAnswerFiles(
        string tree,
        string extension,
        CancellationToken cancellationToken)
    {
        var roomsPath = SafeKnownDirectory(Path.Combine(rootPath, tree, "rooms"), $"{tree}/rooms");
        if (roomsPath is null)
            yield break;

        foreach (var roomPath in SafeGuidDirectories(roomsPath, $"{tree}/rooms", cancellationToken))
        {
            var room = Path.GetFileName(roomPath);
            var questionsPath = SafeKnownDirectory(
                Path.Combine(roomPath, "questions"),
                $"{tree}/rooms/{room}/questions");
            if (questionsPath is null)
                continue;

            foreach (var questionPath in SafeGuidDirectories(
                         questionsPath,
                         $"{tree}/rooms/{room}/questions",
                         cancellationToken))
            {
                var question = Path.GetFileName(questionPath);
                foreach (var answerPath in SafeGuidDirectories(
                             questionPath,
                             $"{tree}/rooms/{room}/questions/{question}",
                             cancellationToken))
                {
                    var answer = Path.GetFileName(answerPath);
                    foreach (var entry in ReadVariants(
                                 answerPath,
                                 $"{tree}/rooms/{room}/questions/{question}/{answer}",
                                 extension,
                                 cancellationToken))
                    {
                        yield return entry;
                    }
                }
            }
        }
    }

    private IEnumerable<LocalMediaFileEntry> ReadVariants(
        string directoryPath,
        string directoryKey,
        string extension,
        CancellationToken cancellationToken)
    {
        foreach (var variant in VariantNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var storageKey = $"{directoryKey}/{variant}{extension}";
            var entry = ReadEntry(storageKey);
            if (entry is not null)
                yield return entry;
        }
    }

    private LocalMediaFileEntry? ReadEntry(string storageKey)
    {
        var path = MediaStoragePathResolver.ResolveStoragePath(rootPath, storageKey);
        if (!IsSafeExistingPath(path, storageKey))
            return null;

        try
        {
            var fileInfo = new FileInfo(path);
            return new LocalMediaFileEntry(
                storageKey,
                new DateTimeOffset(fileInfo.LastWriteTimeUtc, TimeSpan.Zero),
                fileInfo.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailure(storageKey, exception);
            return null;
        }
    }

    private string? SafeKnownDirectory(string path, string storageScope)
    {
        try
        {
            if (!Directory.Exists(path))
                return null;

            return IsReparsePoint(path) ? null : path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailure(storageScope, exception);
            return null;
        }
    }

    private IEnumerable<string> SafeGuidDirectories(
        string parentPath,
        string storageScope,
        CancellationToken cancellationToken)
    {
        string[] directories;
        try
        {
            directories = Directory.GetDirectories(parentPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailure(storageScope, exception);
            yield break;
        }

        foreach (var directory in directories.OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            var isSafe = false;
            try
            {
                isSafe = IsCanonicalGuid(name) && !IsReparsePoint(directory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                LogFailure($"{storageScope}/{name}", exception);
            }

            if (isSafe)
                yield return directory;
        }
    }

    private bool IsSafeExistingPath(string path, string storageKey)
    {
        try
        {
            if (!IsSafeRoot())
                return false;

            var current = rootPath;
            foreach (var segment in storageKey.Split('/').SkipLast(1))
            {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(current) || IsReparsePoint(current))
                    return false;
            }

            return File.Exists(path) && !IsReparsePoint(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailure(storageKey, exception);
            return false;
        }
    }

    private bool IsSafeRoot()
    {
        try
        {
            return Directory.Exists(rootPath) && !IsReparsePoint(rootPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogFailure("storage-root", exception);
            return false;
        }
    }

    private static bool TryParseStorageKey(string storageKey, out string normalizedKey)
    {
        normalizedKey = storageKey.Replace('\\', '/');
        if (!string.Equals(storageKey, normalizedKey, StringComparison.Ordinal) ||
            storageKey.StartsWith('/') ||
            storageKey.Split('/').Any(segment => segment.Length == 0 || segment.StartsWith('.')))
        {
            return false;
        }

        var segments = normalizedKey.Split('/');
        if (segments.Length != 7 ||
            !string.Equals(segments[1], "rooms", StringComparison.Ordinal) ||
            !IsCanonicalGuid(segments[2]) ||
            !IsCanonicalGuid(segments[4]) ||
            !IsCanonicalGuid(segments[5]))
        {
            return false;
        }

        var expectedMiddle = segments[0] == "profile" ? "players" : "questions";
        if (!string.Equals(segments[3], expectedMiddle, StringComparison.Ordinal))
            return false;

        var extension = segments[0] switch
        {
            "profile" or "photo-answer" => ".jpg",
            "drawing-answer" => ".png",
            _ => null
        };
        return extension is not null &&
               (segments[6] == $"display{extension}" ||
                segments[6] == $"thumbnail{extension}");
    }

    private static bool IsCanonicalGuid(string value) =>
        Guid.TryParseExact(value, "N", out var guid) &&
        string.Equals(value, guid.ToString("N"), StringComparison.Ordinal);

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private void LogFailure(string storageKey, Exception exception) =>
        logger.LogWarning(
            "Local media file catalog skipped storage key {StorageKey}; error type {ErrorType}",
            storageKey,
            exception.GetType().Name);
}
