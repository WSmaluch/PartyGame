using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using PartyGame.Infrastructure.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PartyGame.Tests.Infrastructure.Media;

public sealed class DrawingMediaStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PartyGame.DrawingMediaTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ValidPng_PreservesPngTransparency_CreatesVariantsAndSha256()
    {
        var storage = CreateStorage();
        await using var input = await Png(1200, 800, transparent: true, metadata: true);
        var result = await storage.SaveDrawingAsync(Request(input));

        Assert.Equal("image/png", result.ContentType);
        Assert.EndsWith("display.png", result.DisplayStorageKey);
        Assert.EndsWith("thumbnail.png", result.ThumbnailStorageKey);
        Assert.Equal((1200, 800), (result.Width, result.Height));
        await using var display = await storage.OpenReadAsync(result.DisplayStorageKey);
        await using var thumbnail = await storage.OpenReadAsync(result.ThumbnailStorageKey);
        Assert.NotNull(display);
        Assert.NotNull(thumbnail);
        var bytes = await ReadAll(display!);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Sha256);
        using var displayImage = Image.Load<Rgba32>(bytes);
        using var thumbnailImage = await Image.LoadAsync<Rgba32>(thumbnail!);
        Assert.Equal(0, displayImage[0, 0].A);
        Assert.Null(displayImage.Metadata.ExifProfile);
        Assert.Equal(640, thumbnailImage.Width);
        Assert.Equal(427, thumbnailImage.Height);
    }

    [Fact]
    public async Task Resize_DownscalesLargeAndNeverUpscalesSmall()
    {
        var storage = CreateStorage(maxLongEdge: 1000, minimum: 10);
        await using var large = await Png(2000, 1000);
        await using var small = await Png(400, 300);
        var scaled = await storage.SaveDrawingAsync(Request(large));
        var unchanged = await storage.SaveDrawingAsync(Request(small));
        Assert.Equal((1000, 500), (scaled.Width, scaled.Height));
        Assert.Equal((400, 300), (unchanged.Width, unchanged.Height));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("image/svg+xml")]
    public async Task WrongMime_IsRejected(string contentType)
    {
        var storage = CreateStorage();
        await using var input = await Png(400, 400);
        var error = await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SaveDrawingAsync(Request(input, contentType)));
        Assert.Equal("drawing_answer_invalid_content_type", error.Code);
    }

    [Fact]
    public async Task CorruptPngAndJpegDisguisedAsPng_AreRejected()
    {
        var storage = CreateStorage();
        await using var corrupt = new MemoryStream("not-png"u8.ToArray());
        var corruptError = await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SaveDrawingAsync(Request(corrupt)));
        Assert.Equal("drawing_answer_invalid_image", corruptError.Code);
        await using var jpeg = new MemoryStream();
        using (var image = new Image<Rgba32>(400, 400, Color.Black)) await image.SaveAsJpegAsync(jpeg);
        jpeg.Position = 0;
        var jpegError = await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SaveDrawingAsync(Request(jpeg)));
        Assert.Equal("drawing_answer_invalid_image", jpegError.Code);
    }

    [Fact]
    public async Task ByteAndDimensionLimits_AreControlled()
    {
        var storage = CreateStorage(maxBytes: 8, minimum: 320, maximumWidth: 500, maximumHeight: 500);
        await using var ordinary = await Png(400, 400);
        var bytes = await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SaveDrawingAsync(Request(ordinary)));
        Assert.Equal("drawing_answer_file_too_large", bytes.Code);

        storage = CreateStorage(maxBytes: 10_000_000, minimum: 320, maximumWidth: 500, maximumHeight: 500);
        await using var wide = await Png(501, 400);
        await using var tall = await Png(400, 501);
        await using var tiny = await Png(319, 400);
        Assert.Equal("drawing_answer_dimensions_too_large", (await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SaveDrawingAsync(Request(wide)))).Code);
        Assert.Equal("drawing_answer_dimensions_too_large", (await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SaveDrawingAsync(Request(tall)))).Code);
        Assert.Equal("drawing_answer_dimensions_too_small", (await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SaveDrawingAsync(Request(tiny)))).Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlankWhiteOrTransparentCanvas_IsRejected(bool transparent)
    {
        var storage = CreateStorage();
        await using var input = await Png(400, 400, transparent, drawLine: false);
        var error = await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SaveDrawingAsync(Request(input)));
        Assert.Equal("drawing_answer_blank", error.Code);
    }

    [Fact]
    public async Task OpenDeleteMissingAndTraversal_AreSafe()
    {
        var storage = CreateStorage();
        await using var input = await Png(400, 400);
        var result = await storage.SaveDrawingAsync(Request(input));
        Assert.True(await storage.ExistsAsync(result.DisplayStorageKey));
        await storage.DeleteAsync(result.DisplayStorageKey);
        Assert.False(await storage.ExistsAsync(result.DisplayStorageKey));
        Assert.Null(await storage.OpenReadAsync(result.DisplayStorageKey));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync("../../secret"));
    }

    [Fact]
    public void Constructor_CleansOnlyOldTemporaryFiles()
    {
        var temporary = Path.Combine(root, ".tmp");
        var final = Path.Combine(root, "rooms", "keep.png");
        Directory.CreateDirectory(temporary);
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        var old = Path.Combine(temporary, "old.tmp");
        var fresh = Path.Combine(temporary, "fresh.tmp");
        File.WriteAllText(old, "old"); File.WriteAllText(fresh, "fresh"); File.WriteAllText(final, "final");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddHours(-2));
        _ = CreateStorage(retentionMinutes: 60);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(final));
    }

    private LocalMediaStorage CreateStorage(int maxLongEdge = 2048, int minimum = 320, long maxBytes = 5_242_880, int maximumWidth = 4096, int maximumHeight = 4096, int retentionMinutes = 60) =>
        new(Options.Create(new MediaOptions { RootPath = root, TemporaryFileRetentionMinutes = retentionMinutes }), Options.Create(new DrawingMediaOptions { MaximumUploadBytes = maxBytes, MinimumWidth = minimum, MinimumHeight = minimum, MaximumWidth = maximumWidth, MaximumHeight = maximumHeight, NormalizedMaximumLongEdge = maxLongEdge, ThumbnailMaximumLongEdge = 640, MinimumInkPixelRatio = 0.001 }));

    private static DrawingMediaWriteRequest Request(Stream stream, string contentType = "image/png") => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), stream, stream.Length, contentType);

    private static async Task<MemoryStream> Png(int width, int height, bool transparent = false, bool metadata = false, bool drawLine = true)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, transparent ? Color.Transparent : Color.White);
        if (drawLine) for (var y = 0; y < height; y++) for (var x = Math.Max(0, width / 2 - 1); x <= width / 2; x++) image[x, y] = Color.Crimson;
        if (metadata) { image.Metadata.ExifProfile = new ExifProfile(); image.Metadata.ExifProfile.SetValue(ExifTag.Make, "Fixture"); }
        await image.SaveAsPngAsync(stream); stream.Position = 0; return stream;
    }

    private static async Task<byte[]> ReadAll(Stream stream) { using var memory = new MemoryStream(); await stream.CopyToAsync(memory); return memory.ToArray(); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
