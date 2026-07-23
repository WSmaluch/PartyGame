using Microsoft.Extensions.Options;

namespace PartyGame.Tests.Api;

public sealed class MediaStorageHealthConfigurationTests
{
    [Theory]
    [InlineData("MediaStorage:DiagnosticsCacheSeconds", "-1")]
    [InlineData("MediaStorage:CriticalFreePercent", "0")]
    [InlineData("MediaStorage:WarningFreePercent", "5")]
    [InlineData("MediaStorage:WarningFreePercent", "101")]
    public void InvalidDiagnosticsConfiguration_FailsDuringHostStartup(string key, string value)
    {
        using var factory = new PartyGameApiFactory(
            Path.Combine(Path.GetTempPath(), "PartyGame.Stage6B5.Configuration", Guid.NewGuid().ToString("N")),
            settings: new Dictionary<string, string?> { [key] = value });

        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
}
