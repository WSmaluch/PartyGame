using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PartyGame.Infrastructure.Media;

namespace PartyGame.Tests.Infrastructure.Media;

public sealed class LocalMediaStorageProbeTests
{
    [Fact]
    public async Task RunAsync_WritesReadsAndDeletesItsUniqueDiagnosticFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.StorageProbe.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var probe = new LocalMediaStorageProbe(NullLogger<LocalMediaStorageProbe>.Instance);

            var succeeded = await probe.RunAsync(directory);

            Assert.True(succeeded);
            var diagnosticsDirectory = Path.Combine(directory, ".diagnostics");
            Assert.True(Directory.Exists(diagnosticsDirectory));
            Assert.Empty(Directory.EnumerateFiles(diagnosticsDirectory, "*.probe", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ReturnsFalseWhenRootCannotBeUsedAndLeavesNoProbeFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.StorageProbe.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
            await File.WriteAllTextAsync(directory, "not a directory");
            var logger = new CollectingLogger<LocalMediaStorageProbe>();
            var probe = new LocalMediaStorageProbe(logger);

            var succeeded = await probe.RunAsync(directory);

            Assert.False(succeeded);
            Assert.False(Directory.Exists(Path.Combine(directory, ".diagnostics")));
            Assert.DoesNotContain(directory, string.Join(Environment.NewLine, logger.Messages), StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(directory))
                File.Delete(directory);
            else if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_CleansUpTheProbeFileWhenReadingFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "PartyGame.StorageProbe.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var probe = new FailingReadProbe(NullLogger<LocalMediaStorageProbe>.Instance);

            var succeeded = await probe.RunAsync(directory);

            Assert.False(succeeded);
            Assert.NotNull(probe.ProbePath);
            Assert.False(File.Exists(probe.ProbePath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FailingReadProbe(ILogger<LocalMediaStorageProbe> logger) : LocalMediaStorageProbe(logger)
    {
        public string? ProbePath { get; private set; }

        protected override Task<byte[]> ReadProbeFileAsync(string probePath, CancellationToken cancellationToken)
        {
            ProbePath = probePath;
            return Task.FromException<byte[]>(new IOException("Read failure for cleanup test."));
        }
    }

    private sealed class CollectingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
