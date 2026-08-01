using PartyGame.Api.Configuration;

namespace PartyGame.Tests.Api;

public sealed class ReleaseRuntimeConfigurationTests
{
    [Fact]
    public void ResolveRuntimePath_NormalizesRelativePathOutsideContentRootCheck()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), "PartyGame.Tests", Guid.NewGuid().ToString("N"));

        var resolved = ReleaseRuntimeConfiguration.ResolveRuntimePath(
            Path.Combine("runtime", "partygame.db"),
            contentRoot,
            "ReleaseRuntime:DatabasePath",
            mustBeOutsideContentRoot: false);

        Assert.Equal(Path.Combine(contentRoot, "runtime", "partygame.db"), resolved);
    }

    [Fact]
    public void ResolveRuntimePath_RejectsTraversal()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseRuntimeConfiguration.ResolveRuntimePath("../partygame.db", "/publish", "ReleaseRuntime:DatabasePath", false));

        Assert.Contains("traversal", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveRuntimePath_RejectsProductionDataInsidePublishedDirectory()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ReleaseRuntimeConfiguration.ResolveRuntimePath("/publish/data/partygame.db", "/publish", "ReleaseRuntime:DatabasePath", true));

        Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("http://192.168.1.20:5050", true)]
    [InlineData("https://partygame.local", true)]
    [InlineData("*", false)]
    [InlineData("http://partygame.local/path", false)]
    public void IsValidOrigin_RequiresExplicitOrigin(string origin, bool expected)
    {
        Assert.Equal(expected, ReleaseRuntimeConfiguration.IsValidOrigin(origin));
    }
}
