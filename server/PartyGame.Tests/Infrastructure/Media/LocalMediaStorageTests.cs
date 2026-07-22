using Microsoft.Extensions.Options;
using PartyGame.Infrastructure.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PartyGame.Tests.Infrastructure.Media;

public sealed class LocalMediaStorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PartyGame.MediaTests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public async Task SavePhotoAsync_NormalizesToJpeg_CreatesThumbnail_AndStripsExif(string contentType)
    {
        var storage = CreateStorage();
        await using var input = new MemoryStream();
        using (var image = new Image<Rgba32>(1200, 800, Color.CornflowerBlue))
        {
            image.Metadata.ExifProfile = new ExifProfile();
            image.Metadata.ExifProfile.SetValue(ExifTag.Make, "Fixture Camera");
            if (contentType == "image/png") await image.SaveAsPngAsync(input); else await image.SaveAsJpegAsync(input);
        }
        input.Position = 0;

        var result = await storage.SavePhotoAsync(Request(input, contentType));

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(1200, result.Width);
        Assert.Equal(800, result.Height);
        Assert.Equal(64, result.Sha256.Length);
        await using var display = await storage.OpenReadAsync(result.DisplayStorageKey);
        await using var thumbnail = await storage.OpenReadAsync(result.ThumbnailStorageKey);
        Assert.NotNull(display);
        Assert.NotNull(thumbnail);
        using var displayImage = await Image.LoadAsync(display!);
        using var thumbnailImage = await Image.LoadAsync(thumbnail!);
        Assert.Null(displayImage.Metadata.ExifProfile);
        Assert.Equal(640, thumbnailImage.Width);
        Assert.Equal(427, thumbnailImage.Height);
    }

    [Fact]
    public async Task SavePhotoAsync_ScalesLargeImageWithoutUpscalingSmallImage()
    {
        var storage = CreateStorage(maxLongEdge: 1000, minimum: 10);
        await using var large = await ImageStream(2000, 1000);
        var scaled = await storage.SavePhotoAsync(Request(large, "image/jpeg"));
        Assert.Equal((1000, 500), (scaled.Width, scaled.Height));

        await using var small = await ImageStream(400, 300);
        var unchanged = await storage.SavePhotoAsync(Request(small, "image/jpeg"));
        Assert.Equal((400, 300), (unchanged.Width, unchanged.Height));
    }

    [Fact]
    public async Task InvalidImageAndDimensions_ReturnControlledCodes()
    {
        var storage = CreateStorage();
        await using var invalid = new MemoryStream("not an image"u8.ToArray());
        var invalidError = await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SavePhotoAsync(Request(invalid, "image/jpeg")));
        Assert.Equal("photo_answer_invalid_image", invalidError.Code);

        await using var tiny = await ImageStream(100, 100);
        var sizeError = await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SavePhotoAsync(Request(tiny, "image/jpeg")));
        Assert.Equal("photo_answer_dimensions_too_small", sizeError.Code);
    }

    [Fact]
    public async Task OpenReadAndDelete_AreSafeAndIdempotent()
    {
        var storage = CreateStorage(minimum: 10);
        await using var input = await ImageStream(400, 400);
        var result = await storage.SavePhotoAsync(Request(input, "image/jpeg"));
        Assert.True(await storage.ExistsAsync(result.DisplayStorageKey));
        await storage.DeleteAsync(result.DisplayStorageKey);
        await storage.DeleteAsync(result.DisplayStorageKey);
        Assert.False(await storage.ExistsAsync(result.DisplayStorageKey));
        Assert.Null(await storage.OpenReadAsync(result.DisplayStorageKey));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync("../../secret"));
    }

    [Fact]
    public async Task LimitsAndMimeType_AreValidatedBeforeDecode()
    {
        var storage = CreateStorage(maxBytes: 4);
        await using var content = new MemoryStream(new byte[5]);
        var tooLarge = await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SavePhotoAsync(Request(content, "image/jpeg")));
        Assert.Equal("photo_answer_file_too_large", tooLarge.Code);
        content.Position = 0;
        var mime = await Assert.ThrowsAsync<PhotoMediaException>(() => storage.SavePhotoAsync(Request(content, "image/gif", length: 1)));
        Assert.Equal("photo_answer_invalid_content_type", mime.Code);
    }

    private LocalMediaStorage CreateStorage(int maxLongEdge = 2048, int minimum = 320, long maxBytes = 10_485_760) => new(Options.Create(new MediaOptions
    {
        RootPath = root,
        NormalizedMaximumLongEdge = maxLongEdge,
        ThumbnailMaximumLongEdge = 640,
        MinimumImageWidth = minimum,
        MinimumImageHeight = minimum,
        MaximumUploadBytes = maxBytes
    }));

    private static PhotoMediaWriteRequest Request(Stream stream, string contentType, long? length = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), stream, length ?? stream.Length, contentType);

    private static async Task<MemoryStream> ImageStream(int width, int height)
    {
        var stream = new MemoryStream();
        using var image = new Image<Rgba32>(width, height, Color.Orange);
        await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = 90 });
        stream.Position = 0;
        return stream;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
