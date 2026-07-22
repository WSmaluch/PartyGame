using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PartyGame.Infrastructure.Media;

public static class DrawingInkDetector
{
    private const int ColorTolerance = 10;

    public static double CalculateInkPixelRatio(Image<Rgba32> image, string backgroundColor)
    {
        var background = Color.ParseHex(backgroundColor).ToPixel<Rgba32>();
        long inkPixels = 0;
        var totalPixels = checked((long)image.Width * image.Height);

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                foreach (var pixel in row)
                {
                    if (pixel.A == 0) continue;
                    var alpha = pixel.A / 255d;
                    var red = pixel.R * alpha + background.R * (1 - alpha);
                    var green = pixel.G * alpha + background.G * (1 - alpha);
                    var blue = pixel.B * alpha + background.B * (1 - alpha);
                    if (Math.Abs(red - background.R) > ColorTolerance
                        || Math.Abs(green - background.G) > ColorTolerance
                        || Math.Abs(blue - background.B) > ColorTolerance)
                    {
                        inkPixels++;
                    }
                }
            }
        });

        return totalPixels == 0 ? 0 : (double)inkPixels / totalPixels;
    }
}
