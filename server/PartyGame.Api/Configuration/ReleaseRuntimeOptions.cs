namespace PartyGame.Api.Configuration;

/// <summary>
/// Explicit runtime contract for a published PartyGame API. Values are supplied by
/// appsettings.Production.json or the documented PARTYGAME_* environment variables.
/// </summary>
public sealed class ReleaseRuntimeOptions
{
    public const string SectionName = "ReleaseRuntime";

    public string DatabasePath { get; set; } = string.Empty;
    public string MediaRoot { get; set; } = string.Empty;
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string ListeningUrl { get; set; } = string.Empty;
    public string DisplayPublicUrl { get; set; } = string.Empty;
    public string AdminPublicUrl { get; set; } = string.Empty;
    public string[] AllowedOrigins { get; set; } = [];

    // This is intentionally opt-in. Production deployments normally migrate through
    // scripts/migrate-data.sh, which takes the lifecycle lock and pre-migration backup.
    public bool ApplyMigrations { get; set; }
}

public static class ReleaseRuntimeConfiguration
{
    public static IReadOnlyDictionary<string, string?> EnvironmentOverrides()
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Add("PARTYGAME_DATABASE_PATH", "ReleaseRuntime:DatabasePath");
        Add("PARTYGAME_MEDIA_ROOT", "ReleaseRuntime:MediaRoot");
        Add("PARTYGAME_PUBLIC_BASE_URL", "ReleaseRuntime:PublicBaseUrl");
        Add("PARTYGAME_URLS", "ReleaseRuntime:ListeningUrl");
        Add("PARTYGAME_DISPLAY_PUBLIC_URL", "ReleaseRuntime:DisplayPublicUrl");
        Add("PARTYGAME_ADMIN_PUBLIC_URL", "ReleaseRuntime:AdminPublicUrl");
        Add("PARTYGAME_LOG_LEVEL", "Serilog:MinimumLevel:Default");
        Add("PARTYGAME_LOG_DIRECTORY", "Diagnostics:LogDirectory");
        Add("PARTYGAME_LOG_FILE_SIZE_LIMIT_MB", "Diagnostics:LogFileSizeLimitMb");
        Add("PARTYGAME_LOG_RETAINED_FILE_COUNT", "Diagnostics:LogRetainedFileCount");
        Add("PARTYGAME_LOG_FORMAT", "Diagnostics:LogFormat");
        Add("PARTYGAME_SUPPORT_BUNDLE_DIRECTORY", "Diagnostics:SupportBundleDirectory");
        Add("PARTYGAME_APPLY_MIGRATIONS", "ReleaseRuntime:ApplyMigrations");
        Add("PARTYGAME_OPERATOR_TOKEN", "Security:Operator:Token");
        Add("PARTYGAME_ALLOW_INSECURE_LAN_HTTP", "Security:Transport:AllowInsecureLanHttp");
        Add("PARTYGAME_ENABLE_HSTS", "Security:Transport:EnableHsts");
        Add("PARTYGAME_DEPLOYMENT_ENABLED", "Deployment:Enabled");
        Add("PARTYGAME_DISPLAY_ROOT", "Deployment:DisplayRoot");
        Add("PARTYGAME_ADMIN_ROOT", "Deployment:AdminRoot");
        Add("PARTYGAME_PLAYER_ROOT", "Deployment:PlayerRoot");
        Add("PARTYGAME_DISPLAY_PATH_BASE", "Deployment:DisplayPathBase");
        Add("PARTYGAME_ADMIN_PATH_BASE", "Deployment:AdminPathBase");
        Add("PARTYGAME_PLAYER_PATH_BASE", "Deployment:PlayerPathBase");

        var urls = Environment.GetEnvironmentVariable("PARTYGAME_URLS");
        if (!string.IsNullOrWhiteSpace(urls)) values["urls"] = urls;

        var origins = Environment.GetEnvironmentVariable("PARTYGAME_ALLOWED_ORIGINS");
        if (!string.IsNullOrWhiteSpace(origins))
        {
            var index = 0;
            foreach (var origin in origins.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                values[$"ReleaseRuntime:AllowedOrigins:{index}"] = origin;
                values[$"Cors:AllowedOrigins:{index}"] = origin;
                index++;
            }
        }

        return values;

        void Add(string environmentName, string configurationKey)
        {
            var value = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(value)) values[configurationKey] = value;
        }
    }

    public static string ResolveRuntimePath(string configuredPath, string contentRoot, string settingName, bool mustBeOutsideContentRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            throw new InvalidOperationException($"{settingName} is required.");

        var parts = configuredPath.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (!Path.IsPathRooted(configuredPath) && parts.Any(part => part == ".."))
            throw new InvalidOperationException($"{settingName} must not contain '..' path traversal segments.");

        var fullPath = Path.GetFullPath(Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(contentRoot, configuredPath));
        var normalizedContentRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(contentRoot));

        if (mustBeOutsideContentRoot && IsSameOrChildPath(fullPath, normalizedContentRoot))
            throw new InvalidOperationException($"{settingName} must be outside the published application directory.");

        return fullPath;
    }

    public static bool IsValidHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host);

    public static bool IsValidOrigin(string? value)
    {
        if (!IsValidHttpUrl(value) || value is null || value == "*") return false;
        var uri = new Uri(value, UriKind.Absolute);
        return string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/";
    }

    private static bool IsSameOrChildPath(string candidate, string parent)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(candidate);
        return string.Equals(normalizedCandidate, parent, StringComparison.Ordinal)
            || normalizedCandidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
