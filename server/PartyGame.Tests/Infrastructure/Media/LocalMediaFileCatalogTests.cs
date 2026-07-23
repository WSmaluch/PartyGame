using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PartyGame.Infrastructure.Media;

namespace PartyGame.Tests.Infrastructure.Media;

public sealed class LocalMediaFileCatalogTests
{
    [Fact]
    public void EnumerateFinalFiles_ReturnsOnlyExactSupportedPatternsInStableOrder()
    {
        var directory = TemporaryDirectory();
        var root = Path.Combine(directory, "media");
        var catalog = Catalog(root);
        var profile = ProfileKey(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "display");
        var photo = AnswerKey("photo-answer", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "thumbnail", ".jpg");
        var drawing = AnswerKey("drawing-answer", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "display", ".png");

        try
        {
            Create(root, profile);
            Create(root, photo);
            Create(root, drawing);
            Create(root, profile.Replace("display.jpg", "preview.jpg", StringComparison.Ordinal));
            Create(root, profile.Replace("display.jpg", "display.png", StringComparison.Ordinal));
            Create(root, $"unknown/rooms/{Guid.NewGuid():N}/display.jpg");
            Create(root, $".tmp/{Guid.NewGuid():N}.tmp");
            Create(root, $"{Path.GetDirectoryName(profile)!.Replace('\\', '/')}/extra/display.jpg");
            Create(root, ".hidden");

            var keys = catalog.EnumerateFinalFiles().Select(entry => entry.StorageKey).ToList();

            Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
            Assert.Equal([drawing, photo, profile], keys);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Catalog_DoesNotFollowSymlinksOrAllowPathsOutsideRoot()
    {
        var directory = TemporaryDirectory();
        var root = Path.Combine(directory, "media");
        var outside = Path.Combine(directory, "outside");
        var room = Guid.NewGuid();
        var player = Guid.NewGuid();
        var asset = Guid.NewGuid();
        var linkParent = Path.Combine(root, "profile", "rooms", room.ToString("N"), "players", player.ToString("N"));
        var linkPath = Path.Combine(linkParent, asset.ToString("N"));
        var outsideFile = Path.Combine(outside, "display.jpg");

        try
        {
            Directory.CreateDirectory(linkParent);
            Directory.CreateDirectory(outside);
            await File.WriteAllTextAsync(outsideFile, "outside");
            Directory.CreateSymbolicLink(linkPath, outside);
            var catalog = Catalog(root);

            Assert.Empty(catalog.EnumerateFinalFiles());
            Assert.Null(await catalog.GetFinalFileAsync(ProfileKey(room, player, asset, "display")));
            Assert.False(await catalog.DeleteAsync(ProfileKey(room, player, asset, "display")));
            await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.DeleteAsync("../outside/display.jpg"));
            Assert.True(File.Exists(outsideFile));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteAsync_IsIdempotentAndAcceptsOnlyRecognizedRelativeKeys()
    {
        var directory = TemporaryDirectory();
        var root = Path.Combine(directory, "media");
        var key = AnswerKey("drawing-answer", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "thumbnail", ".png");

        try
        {
            Create(root, key);
            var catalog = Catalog(root);

            Assert.True(await catalog.DeleteAsync(key));
            Assert.False(await catalog.DeleteAsync(key));
            Assert.False(File.Exists(MediaStoragePathResolver.ResolveStoragePath(root, key)));
            await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.DeleteAsync(key.Replace("thumbnail.png", "other.png", StringComparison.Ordinal)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static LocalMediaFileCatalog Catalog(string root) =>
        new(
            Options.Create(new MediaOptions { RootPath = root }),
            NullLogger<LocalMediaFileCatalog>.Instance);

    private static string TemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.LocalMediaCatalog.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Create(string root, string key)
    {
        var path = MediaStoragePathResolver.ResolveStoragePath(root, key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, key);
    }

    internal static string ProfileKey(Guid room, Guid player, Guid asset, string variant) =>
        $"profile/rooms/{room:N}/players/{player:N}/{asset:N}/{variant}.jpg";

    internal static string AnswerKey(
        string tree,
        Guid room,
        Guid question,
        Guid answer,
        string variant,
        string extension) =>
        $"{tree}/rooms/{room:N}/questions/{question:N}/{answer:N}/{variant}{extension}";
}
