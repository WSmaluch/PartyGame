namespace PartyGame.Infrastructure.Media;

public sealed class MediaOptions
{
    public const string SectionName = "MediaStorage";
    public string Provider { get; set; } = "Local";
    public string? RootPath { get; set; }
    public long MaximumUploadBytes { get; set; } = 10_485_760;
    public int MaximumImageWidth { get; set; } = 6000;
    public int MaximumImageHeight { get; set; } = 6000;
    public int MinimumImageWidth { get; set; } = 320;
    public int MinimumImageHeight { get; set; } = 320;
    public int NormalizedMaximumLongEdge { get; set; } = 2048;
    public int ThumbnailMaximumLongEdge { get; set; } = 640;
    public int JpegQuality { get; set; } = 85;
    public int ThumbnailJpegQuality { get; set; } = 80;
    public int TemporaryFileRetentionMinutes { get; set; } = 60;
}

public sealed class DrawingMediaOptions
{
    public const string SectionName = "DrawingMedia";
    public long MaximumUploadBytes { get; set; } = 5_242_880;
    public int MinimumWidth { get; set; } = 320;
    public int MinimumHeight { get; set; } = 320;
    public int MaximumWidth { get; set; } = 4096;
    public int MaximumHeight { get; set; } = 4096;
    public int NormalizedMaximumLongEdge { get; set; } = 2048;
    public int ThumbnailMaximumLongEdge { get; set; } = 640;
    public double MinimumInkPixelRatio { get; set; } = 0.001;
    public string BackgroundColor { get; set; } = "#FFFFFF";
}
