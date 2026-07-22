using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PartyGame.Infrastructure.Media;

public sealed class LocalMediaStorage : IMediaStorage
{
    private readonly MediaOptions options;
    private readonly string rootPath;
    private readonly DrawingMediaOptions drawingOptions;

    public LocalMediaStorage(IOptions<MediaOptions> options)
        : this(options, Options.Create(new DrawingMediaOptions()))
    {
    }

    public LocalMediaStorage(IOptions<MediaOptions> options, IOptions<DrawingMediaOptions> drawingOptions)
    {
        this.options = options.Value;
        rootPath = string.IsNullOrWhiteSpace(this.options.RootPath)
            ? Path.Combine(Path.GetTempPath(), "PartyGame", "media")
            : Path.GetFullPath(this.options.RootPath);
        this.drawingOptions = drawingOptions.Value;
        CleanupTemporaryFiles();
    }

    public async Task<StoredMediaResult> SaveDrawingAsync(DrawingMediaWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ByteLength <= 0) throw new PhotoMediaException("drawing_answer_file_empty", "The drawing cannot be empty.");
        if (request.ByteLength > drawingOptions.MaximumUploadBytes) throw new PhotoMediaException("drawing_answer_file_too_large", "The drawing exceeds the upload limit.");
        if (!string.Equals(request.ContentType, "image/png", StringComparison.OrdinalIgnoreCase)) throw new PhotoMediaException("drawing_answer_invalid_content_type", "Only PNG drawings are accepted.");
        Image image;
        try { image = await Image.LoadAsync(request.Content, cancellationToken); }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        { throw new PhotoMediaException("drawing_answer_invalid_image", "The uploaded file is not a valid PNG image."); }
        using (image)
        {
            if (!string.Equals(image.Metadata.DecodedImageFormat?.DefaultMimeType, "image/png", StringComparison.OrdinalIgnoreCase))
                throw new PhotoMediaException("drawing_answer_invalid_image", "The uploaded file contents are not PNG.");
            image.Mutate(context => context.AutoOrient());
            if (image.Width < drawingOptions.MinimumWidth || image.Height < drawingOptions.MinimumHeight) throw new PhotoMediaException("drawing_answer_dimensions_too_small", "The drawing dimensions are too small.");
            if (image.Width > drawingOptions.MaximumWidth || image.Height > drawingOptions.MaximumHeight) throw new PhotoMediaException("drawing_answer_dimensions_too_large", "The drawing dimensions are too large.");
            using var pixels = image.CloneAs<Rgba32>();
            if (DrawingInkDetector.CalculateInkPixelRatio(pixels, drawingOptions.BackgroundColor) < drawingOptions.MinimumInkPixelRatio)
                throw new PhotoMediaException("drawing_answer_blank", "The drawing is blank.");
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;
            ResizeDown(image, drawingOptions.NormalizedMaximumLongEdge);
            var width = image.Width; var height = image.Height;
            var directoryKey = $"rooms/{request.RoomId:N}/questions/{request.QuestionInstanceId:N}/drawings/{request.DrawingAnswerId:N}";
            var displayKey = $"{directoryKey}/display.png"; var thumbnailKey = $"{directoryKey}/thumbnail.png";
            var displayPath = Resolve(displayKey); var thumbnailPath = Resolve(thumbnailKey); Directory.CreateDirectory(Path.GetDirectoryName(displayPath)!);
            try
            {
                await SavePngAtomicAsync(image, displayPath, cancellationToken);
                using var thumbnail = image.CloneAs<Rgba32>();
                ResizeDown(thumbnail, drawingOptions.ThumbnailMaximumLongEdge);
                await SavePngAtomicAsync(thumbnail, thumbnailPath, cancellationToken);
                var bytes = await File.ReadAllBytesAsync(displayPath, cancellationToken);
                return new StoredMediaResult(displayKey, thumbnailKey, width, height, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "image/png");
            }
            catch { TryDelete(displayPath); TryDelete(thumbnailPath); throw; }
        }
    }

    public async Task<StoredMediaResult> SavePhotoAsync(PhotoMediaWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ByteLength <= 0) throw new PhotoMediaException("photo_answer_file_empty", "The photo cannot be empty.");
        if (request.ByteLength > options.MaximumUploadBytes) throw new PhotoMediaException("photo_answer_file_too_large", "The photo exceeds the upload limit.");
        if (request.ContentType is not ("image/jpeg" or "image/png")) throw new PhotoMediaException("photo_answer_invalid_content_type", "Only JPEG and PNG photos are accepted.");

        Image image;
        try
        {
            image = await Image.LoadAsync(request.Content, cancellationToken);
        }
        catch (Exception exception) when (exception is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            throw new PhotoMediaException("photo_answer_invalid_image", "The uploaded file is not a valid supported image.");
        }

        using (image)
        {
            var decodedContentType = image.Metadata.DecodedImageFormat?.DefaultMimeType;
            if (!string.Equals(decodedContentType, request.ContentType, StringComparison.OrdinalIgnoreCase))
                throw new PhotoMediaException("photo_answer_invalid_image", "The uploaded file contents do not match its declared image type.");

            image.Mutate(context => context.AutoOrient());
            if (image.Width < options.MinimumImageWidth || image.Height < options.MinimumImageHeight)
                throw new PhotoMediaException("photo_answer_dimensions_too_small", "The photo dimensions are too small.");
            if (image.Width > options.MaximumImageWidth || image.Height > options.MaximumImageHeight)
                throw new PhotoMediaException("photo_answer_dimensions_too_large", "The photo dimensions are too large.");

            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;
            image.Metadata.IptcProfile = null;

            ResizeDown(image, options.NormalizedMaximumLongEdge);
            var width = image.Width;
            var height = image.Height;
            var directoryKey = $"rooms/{request.RoomId:N}/questions/{request.QuestionInstanceId:N}/{request.PhotoAnswerId:N}";
            var displayKey = $"{directoryKey}/display.jpg";
            var thumbnailKey = $"{directoryKey}/thumbnail.jpg";
            var displayPath = Resolve(displayKey);
            var thumbnailPath = Resolve(thumbnailKey);
            Directory.CreateDirectory(Path.GetDirectoryName(displayPath)!);

            try
            {
                await SaveJpegAsync(image, displayPath, options.JpegQuality, cancellationToken);
                using var thumbnail = image.CloneAs<Rgba32>();
                ResizeDown(thumbnail, options.ThumbnailMaximumLongEdge);
                await SaveJpegAsync(thumbnail, thumbnailPath, options.ThumbnailJpegQuality, cancellationToken);
                var bytes = await File.ReadAllBytesAsync(displayPath, cancellationToken);
                return new StoredMediaResult(displayKey, thumbnailKey, width, height, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            }
            catch
            {
                TryDelete(displayPath);
                TryDelete(thumbnailPath);
                throw;
            }
        }
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(storageKey);
        Stream? stream = File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDelete(Resolve(storageKey));
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(Resolve(storageKey)));
    }

    private static void ResizeDown(Image image, int maximumLongEdge)
    {
        if (Math.Max(image.Width, image.Height) <= maximumLongEdge) return;
        image.Mutate(context => context.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(maximumLongEdge, maximumLongEdge), Sampler = KnownResamplers.Lanczos3 }));
    }

    internal void CleanupTemporaryFiles()
    {
        var temporaryPath = Path.Combine(rootPath, ".tmp");
        if (!Directory.Exists(temporaryPath)) return;
        var cutoff = DateTime.UtcNow.AddMinutes(-Math.Max(1, options.TemporaryFileRetentionMinutes));
        foreach (var file in Directory.EnumerateFiles(temporaryPath, "*.tmp", SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file);
            }
            catch (IOException)
            {
                // A concurrently active temporary file is left for the next bounded sweep.
            }
            catch (UnauthorizedAccessException)
            {
                // Storage may be temporarily read-only; final media are never swept here.
            }
        }
    }

    private static async Task SaveJpegAsync(Image image, string path, int quality, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
        await image.SaveAsJpegAsync(stream, new JpegEncoder { Quality = quality }, cancellationToken);
    }

    private async Task SavePngAtomicAsync(Image image, string path, CancellationToken cancellationToken)
    {
        var temporaryDirectory = Path.Combine(rootPath, ".tmp");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await image.SaveAsPngAsync(stream, new PngEncoder(), cancellationToken);
            }
            File.Move(temporaryPath, path);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey)) throw new InvalidOperationException("The media storage key is invalid.");
        var normalizedRoot = Path.GetFullPath(rootPath) + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(rootPath, storageKey));
        if (!resolved.StartsWith(normalizedRoot, StringComparison.Ordinal)) throw new InvalidOperationException("The media storage key is invalid.");
        return resolved;
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
