using Microsoft.Extensions.Options;

namespace PartyGame.Infrastructure.Media;

public sealed class ProfilePhotoStorage : IProfilePhotoStorage
{
    private readonly string _rootPath;

    public ProfilePhotoStorage(IOptions<MediaOptions> options)
    {
        _rootPath = string.IsNullOrWhiteSpace(options.Value.RootPath)
            ? Path.Combine(Path.GetTempPath(), "PartyGame", "media")
            : Path.GetFullPath(options.Value.RootPath);
    }

    public async Task<string> SaveAsync(string roomCode, Guid playerId, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var extension = contentType == "image/png" ? ".png" : ".jpg";
        var safeRoomCode = new string(roomCode.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        var relativeDirectory = Path.Combine(safeRoomCode, playerId.ToString("N"));
        var storageKey = Path.Combine(relativeDirectory, $"{Guid.NewGuid():N}{extension}");
        var destinationPath = Resolve(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using (var destination = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await content.CopyToAsync(destination, cancellationToken);
        }

        return storageKey.Replace(Path.DirectorySeparatorChar, '/');
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        Stream? result = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true)
            : null;
        return Task.FromResult(result);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        return Task.CompletedTask;
    }

    private string Resolve(string storageKey)
    {
        var normalizedRoot = Path.GetFullPath(_rootPath) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(_rootPath, storageKey));
        if (!resolved.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The media storage key is invalid.");
        }
        return resolved;
    }
}
