using PartyGame.Infrastructure.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace PartyGame.Tests.Infrastructure.Media;

public sealed class DrawingAnswerBlankCanvasTests
{
    [Fact]
    public void WhiteAndTransparentCanvases_HaveNoInk()
    {
        using var white = new Image<Rgba32>(400, 400, Color.White);
        using var transparent = new Image<Rgba32>(400, 400, Color.Transparent);
        Assert.Equal(0, DrawingInkDetector.CalculateInkPixelRatio(white, "#FFFFFF"));
        Assert.Equal(0, DrawingInkDetector.CalculateInkPixelRatio(transparent, "#FFFFFF"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    public void SparsePixels_RemainBelowConfiguredThreshold(int pixelCount)
    {
        using var image = new Image<Rgba32>(400, 400, Color.White);
        for (var index = 0; index < pixelCount; index++) image[index % 400, index / 400] = Color.Black;
        Assert.True(DrawingInkDetector.CalculateInkPixelRatio(image, "#FFFFFF") < 0.001);
    }

    [Theory]
    [InlineData(0, 0, 0, 255)]
    [InlineData(220, 20, 80, 255)]
    [InlineData(0, 70, 220, 128)]
    public void ThinRealLines_AreDetectedInAnyColorOrOpacity(byte red, byte green, byte blue, byte alpha)
    {
        using var image = new Image<Rgba32>(400, 400, Color.White);
        for (var y = 0; y < image.Height; y++)
        {
            image[199, y] = new Rgba32(red, green, blue, alpha);
            image[200, y] = new Rgba32(red, green, blue, alpha);
        }
        Assert.True(DrawingInkDetector.CalculateInkPixelRatio(image, "#FFFFFF") >= 0.001);
    }
}
