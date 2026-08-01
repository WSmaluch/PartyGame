namespace PartyGame.Api.Configuration;

public sealed class DeploymentOptions
{
    public const string SectionName = "Deployment";

    public bool Enabled { get; set; }
    public string DisplayRoot { get; set; } = string.Empty;
    public string AdminRoot { get; set; } = string.Empty;
    public string DisplayPathBase { get; set; } = "/display";
    public string AdminPathBase { get; set; } = "/admin";
}

public static class DeploymentConfiguration
{
    public static string ResolveStaticRoot(string configuredPath, string contentRoot, string settingName)
    {
        var resolved = ReleaseRuntimeConfiguration.ResolveRuntimePath(
            configuredPath, contentRoot, settingName, mustBeOutsideContentRoot: false);
        return resolved;
    }

    public static bool IsValidPathBase(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.StartsWith("/", StringComparison.Ordinal)
        && value.Length > 1
        && !value.Contains("..", StringComparison.Ordinal)
        && !value.Contains('\\')
        && !value.EndsWith("/", StringComparison.Ordinal);
}
