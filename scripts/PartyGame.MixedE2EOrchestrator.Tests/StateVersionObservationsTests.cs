using System.Text.Json;
using PartyGame.MixedE2EOrchestrator;
using Xunit;

namespace PartyGame.MixedE2EOrchestrator.Tests;

public sealed class StateVersionObservationsTests
{
    [Fact]
    public void TrackerAcceptsMonotonicVersionsIncludingDuplicates()
    {
        var tracker = new StateVersionTracker();

        Assert.True(tracker.TryAccept(10));
        Assert.True(tracker.TryAccept(11));
        Assert.False(tracker.TryAccept(11));
        Assert.True(tracker.TryAccept(12));

        Assert.Equal(12, tracker.LastAcceptedStateVersion);
        Assert.Equal(3, tracker.ObservationCount);
        Assert.Equal(0, tracker.RegressionCount);
    }

    [Fact]
    public void TrackerRejectsRegressionWithoutChangingAcceptedVersion()
    {
        var tracker = new StateVersionTracker();
        tracker.TryAccept(10);
        tracker.TryAccept(12);

        Assert.False(tracker.TryAccept(11));
        Assert.Equal(12, tracker.LastAcceptedStateVersion);
        Assert.Equal(2, tracker.ObservationCount);
        Assert.Equal(1, tracker.RegressionCount);
    }

    [Fact]
    public void PlayerRecordersAreIndependentAndUseSeparateFiles()
    {
        using var directory = TemporaryDirectory.Create();
        var playerA = new ClientStateVersionRecorder("scripted-player-a", directory.Path);
        var playerB = new ClientStateVersionRecorder("scripted-player-b", directory.Path);

        Assert.True(playerA.Observe(Snapshot(10), "snapshot-accepted"));
        Assert.True(playerB.Observe(Snapshot(20), "snapshot-accepted"));
        Assert.Throws<InvalidOperationException>(() => playerA.Observe(Snapshot(9), "snapshot-accepted"));

        Assert.Equal(10, playerA.Tracker.LastAcceptedStateVersion);
        Assert.Equal(20, playerB.Tracker.LastAcceptedStateVersion);
        Assert.Equal(1, playerA.Tracker.RegressionCount);
        Assert.Equal(0, playerB.Tracker.RegressionCount);
        Assert.True(File.Exists(System.IO.Path.Combine(directory.Path, "scripted-player-a-observation-000001.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(directory.Path, "scripted-player-b-observation-000001.json")));
    }

    [Fact]
    public void WriterUsesNumberedFilesDecodesThemAndLeavesNoTemporaryFile()
    {
        using var directory = TemporaryDirectory.Create();
        var writer = new StateVersionObservationWriter("backend", directory.Path);

        var first = writer.Write(Observation("backend", 10));
        var second = writer.Write(Observation("backend", 11));

        Assert.EndsWith("backend-observation-000001.json", first);
        Assert.EndsWith("backend-observation-000002.json", second);
        Assert.Equal(10, JsonSerializer.Deserialize<StateVersionObservation>(File.ReadAllText(first), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.StateVersion);
        Assert.Equal(11, JsonSerializer.Deserialize<StateVersionObservation>(File.ReadAllText(second), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.StateVersion);
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public void WriterRejectsFilenameCollisionWithoutOverwriting()
    {
        using var directory = TemporaryDirectory.Create();
        var target = System.IO.Path.Combine(directory.Path, "backend-observation-000001.json");
        File.WriteAllText(target, "existing");
        var writer = new StateVersionObservationWriter("backend", directory.Path);

        Assert.Throws<IOException>(() => writer.Write(Observation("backend", 10)));
        Assert.Equal("existing", File.ReadAllText(target));
    }

    [Theory]
    [InlineData("", "event", 1, "Lobby", "")]
    [InlineData("backend", "", 1, "Lobby", "")]
    [InlineData("backend", "event", -1, "Lobby", "")]
    public void ModelRejectsEmptyRequiredValuesAndNegativeVersion(string client, string @event, long stateVersion, string phase, string questionId)
    {
        Assert.ThrowsAny<ArgumentException>(() => new StateVersionObservation(client, @event, stateVersion, phase, questionId, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ModelRejectsMissingPhaseOrQuestionId()
    {
        Assert.Throws<ArgumentNullException>(() => new StateVersionObservation("backend", "event", 1, null!, "", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentNullException>(() => new StateVersionObservation("backend", "event", 1, "Lobby", null!, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void BackendTrackerAcceptsLobbyStartedCompletedAndFailsOnRegression()
    {
        using var directory = TemporaryDirectory.Create();
        var backend = new ClientStateVersionRecorder("backend", directory.Path);

        Assert.True(backend.Observe(Snapshot(10, "Lobby"), "snapshot-accepted"));
        Assert.True(backend.Observe(Snapshot(11, "Started"), "snapshot-accepted"));
        Assert.True(backend.Observe(Snapshot(12, "Completed"), "snapshot-accepted"));
        Assert.Throws<InvalidOperationException>(() => backend.Observe(Snapshot(11, "Started"), "snapshot-accepted"));

        Assert.Equal(12, backend.Tracker.LastAcceptedStateVersion);
        Assert.Equal(1, backend.Tracker.RegressionCount);
    }

    private static StateVersionObservation Observation(string client, long version) =>
        new(client, "snapshot-accepted", version, "Lobby", "", DateTimeOffset.UtcNow);

    private static JsonElement Snapshot(long stateVersion, string phase = "Started") =>
        JsonDocument.Parse($$"""{"stateVersion":{{stateVersion}},"phase":"{{phase}}","game":null}""").RootElement.Clone();

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }
        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"partygame-observation-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
