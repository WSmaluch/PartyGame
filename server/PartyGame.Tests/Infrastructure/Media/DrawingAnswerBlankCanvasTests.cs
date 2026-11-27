using PartyGame.Infrastructure.Media;
using SkiaSharp;

namespace PartyGame.Tests.Infrastructure.Media;

public sealed class DrawingAnswerBlankCanvasTests
{
    [Fact]
    public void WhiteAndTransparentCanvases_HaveNoInk()
    {
        using var white = Canvas(SKColors.White);
        using var transparent = Canvas(SKColors.Transparent);
        Assert.Equal(0, DrawingInkDetector.CalculateInkPixelRatio(white, "#FFFFFF"));
        Assert.Equal(0, DrawingInkDetector.CalculateInkPixelRatio(transparent, "#FFFFFF"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void SparsePixels_RemainBelowConfiguredThreshold(int pixelCount)
    {
        using var image = Canvas(SKColors.White);
        for (var index = 0; index < pixelCount; index++) image.SetPixel(index % 400, index / 400, SKColors.Black);
        Assert.True(DrawingInkDetector.CalculateInkPixelRatio(image, "#FFFFFF") < 0.001);
    }

    [Theory]
    [InlineData(0, 0, 0, 255)]
    [InlineData(220, 20, 80, 255)]
    [InlineData(0, 70, 220, 128)]
    public void ThinRealLines_AreDetectedInAnyColorOrOpacity(byte red, byte green, byte blue, byte alpha)
    {
        using var image = Canvas(SKColors.White);
        using (var canvas = new SKCanvas(image))
        using (var paint = new SKPaint { Color = new SKColor(red, green, blue, alpha), BlendMode = SKBlendMode.Src })
        {
            canvas.DrawRect(new SKRect(199, 0, 201, 400), paint);
        }
        Assert.True(DrawingInkDetector.CalculateInkPixelRatio(image, "#FFFFFF") >= 0.001);
    }

    private static SKBitmap Canvas(SKColor background)
    {
        var bitmap = new SKBitmap(new SKImageInfo(400, 400, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(background);
        return bitmap;
    }
}
