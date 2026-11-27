using SkiaSharp;

namespace PartyGame.Infrastructure.Media;

/// <summary>Header-level facts read before any pixel is decoded.</summary>
internal sealed record ImageHeader(string? ContentType, int Width, int Height);

/// <summary>
/// Image decoding and re-encoding for untrusted uploads. Dimensions are read
/// from the header first so oversized images are rejected before their pixels
/// are allocated; output is re-encoded from pixels only, so EXIF/XMP/IPTC never
/// survive and no ICC profile is embedded (pixels are converted to sRGB).
/// </summary>
internal sealed class SkiaImageProcessor : IDisposable
{
    private readonly SKData data;
    private readonly SKCodec codec;

    private SkiaImageProcessor(SKData data, SKCodec codec)
    {
        this.data = data;
        this.codec = codec;
        var width = codec.Info.Width;
        var height = codec.Info.Height;
        if (SwapsAxes(codec.EncodedOrigin)) (width, height) = (height, width);
        Header = new ImageHeader(ContentTypeOf(codec.EncodedFormat), width, height);
    }

    /// <summary>Oriented dimensions and detected type, from the header only.</summary>
    public ImageHeader Header { get; }

    /// <summary>Returns null when the stream is not an image Skia can parse.</summary>
    public static SkiaImageProcessor? Open(Stream content)
    {
        var data = SKData.Create(content);
        if (data is null || data.Size == 0)
        {
            data?.Dispose();
            return null;
        }
        var codec = SKCodec.Create(data);
        if (codec is null)
        {
            data.Dispose();
            return null;
        }
        return new SkiaImageProcessor(data, codec);
    }

    /// <summary>Decodes to premultiplied sRGB RGBA and applies the EXIF orientation.</summary>
    public SKBitmap? DecodeOriented()
    {
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());
        var decoded = new SKBitmap(info);
        var result = codec.GetPixels(info, decoded.GetPixels());
        if (result != SKCodecResult.Success)
        {
            decoded.Dispose();
            return null;
        }
        if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft) return decoded;
        using (decoded) return Orient(decoded, codec.EncodedOrigin);
    }

    public void Dispose()
    {
        codec.Dispose();
        data.Dispose();
    }

    /// <summary>Scales down (never up) so the long edge fits; returns a new bitmap or the same one.</summary>
    public static SKBitmap ResizeDown(SKBitmap source, int maximumLongEdge)
    {
        var longEdge = Math.Max(source.Width, source.Height);
        if (longEdge <= maximumLongEdge) return source;
        var scale = (double)maximumLongEdge / longEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = source.Resize(source.Info.WithSize(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return resized ?? throw new InvalidOperationException("The image could not be resized.");
    }

    /// <summary>Composites onto an opaque background; JPEG has no alpha channel.</summary>
    public static SKBitmap Flatten(SKBitmap source, SKColor background)
    {
        var flattened = new SKBitmap(source.Info.WithAlphaType(SKAlphaType.Opaque));
        using var canvas = new SKCanvas(flattened);
        canvas.Clear(background);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return flattened;
    }

    public static SKData EncodeJpeg(SKBitmap bitmap, int quality) =>
        Encode(bitmap, pixmap => pixmap.Encode(new SKJpegEncoderOptions(Math.Clamp(quality, 1, 100), SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore)));

    public static SKData EncodePng(SKBitmap bitmap) =>
        Encode(bitmap, pixmap => pixmap.Encode(SKPngEncoderOptions.Default));

    private static SKData Encode(SKBitmap bitmap, Func<SKPixmap, SKData?> encode)
    {
        // Reinterpret the already-sRGB pixels without a colour space so the
        // encoder does not embed an ICC profile.
        using var pixmap = new SKPixmap(bitmap.Info.WithColorSpace(null), bitmap.GetPixels(), bitmap.RowBytes);
        return encode(pixmap) ?? throw new InvalidOperationException("The image could not be encoded.");
    }

    private static string? ContentTypeOf(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => "image/jpeg",
        SKEncodedImageFormat.Png => "image/png",
        _ => null
    };

    private static bool SwapsAxes(SKEncodedOrigin origin) =>
        origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        var width = source.Width;
        var height = source.Height;
        var swap = SwapsAxes(origin);
        var oriented = new SKBitmap(source.Info.WithSize(swap ? height : width, swap ? width : height));
        using var canvas = new SKCanvas(oriented);
        canvas.Clear(SKColors.Transparent);
        switch (origin)
        {
            case SKEncodedOrigin.TopRight: canvas.Translate(width, 0); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.BottomRight: canvas.Translate(width, height); canvas.RotateDegrees(180); break;
            case SKEncodedOrigin.BottomLeft: canvas.Translate(0, height); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
            case SKEncodedOrigin.RightTop: canvas.Translate(height, 0); canvas.RotateDegrees(90); break;
            case SKEncodedOrigin.RightBottom: canvas.Translate(height, width); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
            case SKEncodedOrigin.LeftBottom: canvas.Translate(0, width); canvas.RotateDegrees(270); break;
        }
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return oriented;
    }
}
