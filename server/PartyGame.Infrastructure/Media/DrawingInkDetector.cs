using SkiaSharp;

namespace PartyGame.Infrastructure.Media;

public static class DrawingInkDetector
{
    private const int ColorTolerance = 10;

    /// <summary>
    /// Share of pixels that differ visibly from the background after compositing
    /// onto it. Accepts any bitmap; pixels are read as premultiplied RGBA.
    /// </summary>
    public static double CalculateInkPixelRatio(SKBitmap image, string backgroundColor)
    {
        if (!SKColor.TryParse(backgroundColor, out var background))
            throw new ArgumentException("The background colour is invalid.", nameof(backgroundColor));
        var totalPixels = checked((long)image.Width * image.Height);
        if (totalPixels == 0) return 0;

        using var rgba = ToPremultipliedRgba(image);
        var pixels = rgba.GetPixelSpan();
        long inkPixels = 0;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var alphaByte = pixels[index + 3];
            if (alphaByte == 0) continue;
            var transparency = 1 - alphaByte / 255d;
            // Premultiplied colour plus the background showing through.
            var red = pixels[index] + background.Red * transparency;
            var green = pixels[index + 1] + background.Green * transparency;
            var blue = pixels[index + 2] + background.Blue * transparency;
            if (Math.Abs(red - background.Red) > ColorTolerance
                || Math.Abs(green - background.Green) > ColorTolerance
                || Math.Abs(blue - background.Blue) > ColorTolerance)
            {
                inkPixels++;
            }
        }
        return (double)inkPixels / totalPixels;
    }

    private static SKBitmap ToPremultipliedRgba(SKBitmap image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var converted = new SKBitmap(info);
        // Drawing through a canvas converts any colour/alpha type to premultiplied RGBA.
        using var canvas = new SKCanvas(converted);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        canvas.DrawBitmap(image, 0, 0, paint);
        canvas.Flush();
        return converted;
    }
}
