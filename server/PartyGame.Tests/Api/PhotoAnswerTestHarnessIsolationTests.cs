namespace PartyGame.Tests.Api;

public sealed class PhotoAnswerTestHarnessIsolationTests
{
    [Fact]
    public async Task Harnesses_UseIndependentRuntimeRootsAndCleanupTheirSqliteFiles()
    {
        var first = new PhotoAnswerTestHarness();
        var second = new PhotoAnswerTestHarness();
        var firstRoot = first.Factory.TemporaryDirectory;
        var secondRoot = second.Factory.TemporaryDirectory;
        var firstDatabase = first.Factory.DatabasePath;
        var secondDatabase = second.Factory.DatabasePath;

        try
        {
            Assert.NotEqual(firstRoot, secondRoot);
            Assert.NotEqual(firstDatabase, secondDatabase);
            Assert.NotEqual(first.Factory.MediaRootPath, second.Factory.MediaRootPath);
            Assert.True(File.Exists(firstDatabase));
            Assert.True(File.Exists(secondDatabase));
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
        }

        Assert.False(Directory.Exists(firstRoot));
        Assert.False(Directory.Exists(secondRoot));
        Assert.False(File.Exists(firstDatabase));
        Assert.False(File.Exists(secondDatabase));
    }
}
