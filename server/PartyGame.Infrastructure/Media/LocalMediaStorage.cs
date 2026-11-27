using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using SkiaSharp;

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
        rootPath = MediaStoragePathResolver.ResolveRootPath(this.options.RootPath);
        this.drawingOptions = drawingOptions.Value;
        Directory.CreateDirectory(rootPath);
        CleanupTemporaryFiles();
    }

    public Task<StoredMediaResult> SaveProfilePhotoAsync(ProfilePhotoMediaWriteRequest request, CancellationToken cancellationToken = default) =>
        SaveJpegMediaAsync(
            request.Content,
            request.ByteLength,
            request.ContentType,
            $"profile/rooms/{request.RoomId:N}/players/{request.PlayerId:N}/{request.MediaAssetId:N}",
            "profile_photo",
            options.ProfilePhotoMaximumUploadBytes,
            cancellationToken);

    public async Task<StoredMediaResult> SaveDrawingAsync(DrawingMediaWriteRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ByteLength <= 0) throw new PhotoMediaException("drawing_answer_file_empty", "The drawing cannot be empty.");
        if (request.ByteLength > drawingOptions.MaximumUploadBytes) throw new PhotoMediaException("drawing_answer_file_too_large", "The drawing exceeds the upload limit.");
        if (!string.Equals(request.ContentType, "image/png", StringComparison.OrdinalIgnoreCase)) throw new PhotoMediaException("drawing_answer_invalid_content_type", "Only PNG drawings are accepted.");
        await EnsureSignatureAsync(request.Content, request.ContentType, "drawing_answer", cancellationToken);
        using var processor = SkiaImageProcessor.Open(request.Content)
            ?? throw new PhotoMediaException("drawing_answer_invalid_image", "The uploaded file is not a valid PNG image.");
        var header = processor.Header;
        if (!string.Equals(header.ContentType, "image/png", StringComparison.OrdinalIgnoreCase))
            throw new PhotoMediaException("drawing_answer_invalid_image", "The uploaded file contents are not PNG.");
        if (header.Width < drawingOptions.MinimumWidth || header.Height < drawingOptions.MinimumHeight) throw new PhotoMediaException("drawing_answer_dimensions_too_small", "The drawing dimensions are too small.");
        if (header.Width > drawingOptions.MaximumWidth || header.Height > drawingOptions.MaximumHeight) throw new PhotoMediaException("drawing_answer_dimensions_too_large", "The drawing dimensions are too large.");
        using var decoded = processor.DecodeOriented()
            ?? throw new PhotoMediaException("drawing_answer_invalid_image", "The uploaded file is not a valid PNG image.");
        if (DrawingInkDetector.CalculateInkPixelRatio(decoded, drawingOptions.BackgroundColor) < drawingOptions.MinimumInkPixelRatio)
            throw new PhotoMediaException("drawing_answer_blank", "The drawing is blank.");
        var display = SkiaImageProcessor.ResizeDown(decoded, drawingOptions.NormalizedMaximumLongEdge);
        try
        {
            var width = display.Width; var height = display.Height;
            var directoryKey = $"drawing-answer/rooms/{request.RoomId:N}/questions/{request.QuestionInstanceId:N}/{request.DrawingAnswerId:N}";
            var displayKey = $"{directoryKey}/display.png"; var thumbnailKey = $"{directoryKey}/thumbnail.png";
            var displayPath = Resolve(displayKey); var thumbnailPath = Resolve(thumbnailKey); Directory.CreateDirectory(Path.GetDirectoryName(displayPath)!);
            try
            {
                using (var encoded = SkiaImageProcessor.EncodePng(display))
                    await SaveDataAtomicAsync(encoded, displayPath, cancellationToken);
                var thumbnail = SkiaImageProcessor.ResizeDown(display, drawingOptions.ThumbnailMaximumLongEdge);
                try
                {
                    using var encodedThumbnail = SkiaImageProcessor.EncodePng(thumbnail);
                    await SaveDataAtomicAsync(encodedThumbnail, thumbnailPath, cancellationToken);
                }
                finally { if (!ReferenceEquals(thumbnail, display)) thumbnail.Dispose(); }
                var (byteLength, sha256) = await CalculateHashAsync(displayPath, cancellationToken);
                return new StoredMediaResult(displayKey, thumbnailKey, width, height, byteLength, sha256, "image/png");
            }
            catch { TryDelete(displayPath); TryDelete(thumbnailPath); throw; }
        }
        finally { if (!ReferenceEquals(display, decoded)) display.Dispose(); }
    }

    public Task<StoredMediaResult> SavePhotoAsync(PhotoMediaWriteRequest request, CancellationToken cancellationToken = default) =>
        SaveJpegMediaAsync(
            request.Content,
            request.ByteLength,
            request.ContentType,
            $"photo-answer/rooms/{request.RoomId:N}/questions/{request.QuestionInstanceId:N}/{request.PhotoAnswerId:N}",
            "photo_answer",
            options.MaximumUploadBytes,
            cancellationToken);

    private async Task<StoredMediaResult> SaveJpegMediaAsync(
        Stream content,
        long byteLength,
        string contentType,
        string directoryKey,
        string errorPrefix,
        long maximumUploadBytes,
        CancellationToken cancellationToken)
    {
        if (byteLength <= 0) throw new PhotoMediaException($"{errorPrefix}_file_empty", "The image cannot be empty.");
        if (byteLength > maximumUploadBytes) throw new PhotoMediaException($"{errorPrefix}_file_too_large", "The image exceeds the upload limit.");
        if (contentType is not ("image/jpeg" or "image/png")) throw new PhotoMediaException($"{errorPrefix}_invalid_content_type", "Only JPEG and PNG images are accepted.");
        await EnsureSignatureAsync(content, contentType, errorPrefix, cancellationToken);

        using var processor = SkiaImageProcessor.Open(content)
            ?? throw new PhotoMediaException($"{errorPrefix}_invalid_image", "The uploaded file is not a valid supported image.");
        var header = processor.Header;
        if (!string.Equals(header.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
            throw new PhotoMediaException($"{errorPrefix}_invalid_image", "The uploaded file contents do not match its declared image type.");
        if (header.Width < options.MinimumImageWidth || header.Height < options.MinimumImageHeight)
            throw new PhotoMediaException($"{errorPrefix}_dimensions_too_small", "The image dimensions are too small.");
        if (header.Width > options.MaximumImageWidth || header.Height > options.MaximumImageHeight)
            throw new PhotoMediaException($"{errorPrefix}_dimensions_too_large", "The image dimensions are too large.");

        using var decoded = processor.DecodeOriented()
            ?? throw new PhotoMediaException($"{errorPrefix}_invalid_image", "The uploaded file is not a valid supported image.");
        // JPEG output has no alpha channel; transparent PNG areas become white.
        using var opaque = SkiaImageProcessor.Flatten(decoded, SKColors.White);
        var display = SkiaImageProcessor.ResizeDown(opaque, options.NormalizedMaximumLongEdge);
        try
        {
            var width = display.Width;
            var height = display.Height;
            var displayKey = $"{directoryKey}/display.jpg";
            var thumbnailKey = $"{directoryKey}/thumbnail.jpg";
            var displayPath = Resolve(displayKey);
            var thumbnailPath = Resolve(thumbnailKey);
            Directory.CreateDirectory(Path.GetDirectoryName(displayPath)!);

            try
            {
                using (var encoded = SkiaImageProcessor.EncodeJpeg(display, options.JpegQuality))
                    await SaveDataAtomicAsync(encoded, displayPath, cancellationToken);
                var thumbnail = SkiaImageProcessor.ResizeDown(display, options.ThumbnailMaximumLongEdge);
                try
                {
                    using var encodedThumbnail = SkiaImageProcessor.EncodeJpeg(thumbnail, options.ThumbnailJpegQuality);
                    await SaveDataAtomicAsync(encodedThumbnail, thumbnailPath, cancellationToken);
                }
                finally { if (!ReferenceEquals(thumbnail, display)) thumbnail.Dispose(); }
                var (storedLength, sha256) = await CalculateHashAsync(displayPath, cancellationToken);
                return new StoredMediaResult(displayKey, thumbnailKey, width, height, storedLength, sha256);
            }
            catch
            {
                TryDelete(displayPath);
                TryDelete(thumbnailPath);
                throw;
            }
        }
        finally { if (!ReferenceEquals(display, opaque)) display.Dispose(); }
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

    private Task SaveDataAtomicAsync(SKData data, string path, CancellationToken cancellationToken) =>
        SaveAtomicAsync(path, async stream => await stream.WriteAsync(data.AsSpan().ToArray(), cancellationToken), cancellationToken);

    private async Task SaveAtomicAsync(string path, Func<Stream, Task> writeAsync, CancellationToken cancellationToken)
    {
        var temporaryDirectory = Path.Combine(rootPath, ".tmp");
        Directory.CreateDirectory(temporaryDirectory);
        var temporaryPath = Path.Combine(temporaryDirectory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                await writeAsync(stream);
            }
            File.Move(temporaryPath, path);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static async Task EnsureSignatureAsync(Stream content, string contentType, string errorPrefix, CancellationToken cancellationToken)
    {
        if (!content.CanSeek)
            throw new PhotoMediaException($"{errorPrefix}_invalid_image", "The uploaded image stream cannot be validated.");

        var position = content.Position;
        try
        {
            var signature = new byte[8];
            var read = await content.ReadAsync(signature.AsMemory(), cancellationToken);
            var valid = contentType switch
            {
                "image/jpeg" => read >= 3 && signature[0] == 0xff && signature[1] == 0xd8 && signature[2] == 0xff,
                "image/png" => read == 8 && signature.AsSpan().SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
                _ => false
            };
            if (!valid) throw new PhotoMediaException($"{errorPrefix}_invalid_image", "The uploaded file signature does not match its declared image type.");
        }
        finally
        {
            content.Position = position;
        }
    }

    private static async Task<(long ByteLength, string Sha256)> CalculateHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return (stream.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }

    private string Resolve(string storageKey)
    {
        return MediaStoragePathResolver.ResolveStoragePath(rootPath, storageKey);
    }

    private static void TryDelete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
