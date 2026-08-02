using System.Reflection;

namespace PartyGame.Api.Diagnostics;

public sealed class BuildVersionInfo(IHostEnvironment environment)
{
    public const string ApiContractVersion = "1";
    public const string SignalRContractVersion = ApiContractVersion;
    public const string BackupFormatVersion = "1";
    public const string SupportBundleFormatVersion = "1";

    private readonly Assembly assembly = Assembly.GetExecutingAssembly();

    public string ApplicationVersion => InformationalVersion.Split('+', 2)[0];
    public string InformationalVersion => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    public string CommitHash => Metadata("CommitHash") ?? "unknown";
    public string BuildTimestampUtc => Metadata("BuildTimestampUtc") ?? "unknown";
    public string Environment => environment.EnvironmentName;

    public VersionContract ToContract(string databaseSchemaVersion, string? displayVersion = null, string? adminVersion = null) => new(
        ApplicationVersion, InformationalVersion, CommitHash, BuildTimestampUtc, Environment,
        ApiContractVersion, SignalRContractVersion, databaseSchemaVersion, BackupFormatVersion,
        SupportBundleFormatVersion, displayVersion ?? ApplicationVersion, adminVersion ?? ApplicationVersion,
        null, null);

    private string? Metadata(string key) => assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => string.Equals(attribute.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
}

public sealed record VersionContract(
    string ApplicationVersion,
    string InformationalVersion,
    string CommitHash,
    string BuildTimestampUtc,
    string Environment,
    string ApiContractVersion,
    string SignalRContractVersion,
    string DatabaseSchemaVersion,
    string BackupFormatVersion,
    string SupportBundleFormatVersion,
    string DisplayVersion,
    string AdminVersion,
    string? IosClientVersion,
    string? MinimumSupportedIosVersion);
