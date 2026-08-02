namespace PartyGame.Api.Diagnostics;

public sealed class DiagnosticsOptions
{
    public const string SectionName = "Diagnostics";
    public string LogDirectory { get; set; } = "data/logs";
    public int LogFileSizeLimitMb { get; set; } = 10;
    public int LogRetainedFileCount { get; set; } = 14;
    public string LogFormat { get; set; } = "json";
    public string SupportBundleDirectory { get; set; } = "data/support-bundles";

    public long LogFileSizeLimitBytes => (long)LogFileSizeLimitMb * 1024 * 1024;
    public bool IsJson => string.Equals(LogFormat, "json", StringComparison.OrdinalIgnoreCase);
}
