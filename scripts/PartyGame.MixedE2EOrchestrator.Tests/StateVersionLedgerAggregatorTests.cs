using System.Text.Json;
using PartyGame.MixedE2EOrchestrator;
using Xunit;

namespace PartyGame.MixedE2EOrchestrator.Tests;

public sealed class StateVersionLedgerAggregatorTests
{
    [Fact]
    public void AggregatesFiveClientsAndReconnectsIntoPassingLedger()
    {
        using var fixture = LedgerFixture.Create();

        var ledger = fixture.Aggregate();

        Assert.True(ledger.Passed);
        Assert.Equal(0, ledger.FailureCount);
        Assert.Equal(50, ledger.FinalBackendStateVersion);
        Assert.Equal(20, ledger.Clients["ios"].VersionBeforeDisconnect);
        Assert.Equal(21, ledger.Clients["ios"].RecoveredVersion);
        Assert.True(ledger.Clients["ios"].RecoveryVersionPassed);
        Assert.Equal(30, ledger.Clients["display"].VersionBeforeDisconnect);
        Assert.Equal(31, ledger.Clients["display"].RecoveredVersion);
        Assert.True(ledger.Clients["display"].RecoveryVersionPassed);
    }

    [Theory]
    [InlineData("ios")]
    [InlineData("display")]
    [InlineData("scripted-player-a")]
    [InlineData("scripted-player-b")]
    [InlineData("backend")]
    public void FailsWhenAnyRequiredClientIsMissing(string client)
    {
        using var fixture = LedgerFixture.Create();
        fixture.DeleteClient(client);

        var ledger = fixture.Aggregate();

        Assert.False(ledger.Passed);
        Assert.Contains(ledger.Failures, failure => failure.Code == "missing-client-observations" && failure.Client == client);
    }

    [Theory]
    [InlineData("ios")]
    [InlineData("display")]
    [InlineData("scripted-player-a")]
    [InlineData("scripted-player-b")]
    [InlineData("backend")]
    public void FailsOnStateVersionRegressionForEveryClient(string client)
    {
        using var fixture = LedgerFixture.Create();
        fixture.Write(client, 99, 11, "regression", phase: "Completed");

        var ledger = fixture.Aggregate();

        Assert.False(ledger.Passed);
        Assert.Equal(1, ledger.Clients[client].RegressionCount);
        Assert.Contains(ledger.Failures, failure => failure.Code == "state-version-regression" && failure.Client == client);
    }

    [Theory]
    [InlineData("snapshot-before-disconnect", "snapshot-after-recovery")]
    [InlineData("snapshot-before-reload", "snapshot-after-reconnect")]
    public void FailsWhenReconnectVersionMovesBackwards(string beforeEvent, string recoveredEvent)
    {
        using var fixture = LedgerFixture.Create();
        var client = beforeEvent.Contains("disconnect", StringComparison.Ordinal) ? "ios" : "display";
        fixture.ReplaceReconnect(client, beforeEvent, 40, recoveredEvent, 39);

        var ledger = fixture.Aggregate();

        Assert.False(ledger.Passed);
        Assert.Contains(ledger.Failures, failure => failure.Code == "recovery-version-regression" && failure.Client == client);
    }

    [Theory]
    [InlineData("ios", "snapshot-before-disconnect")]
    [InlineData("ios", "snapshot-after-recovery")]
    [InlineData("display", "snapshot-before-reload")]
    [InlineData("display", "snapshot-after-reconnect")]
    public void FailsWhenRequiredReconnectEventIsMissing(string client, string @event)
    {
        using var fixture = LedgerFixture.Create();
        fixture.RemoveEvent(client, @event);

        var ledger = fixture.Aggregate();

        Assert.False(ledger.Passed);
        Assert.Contains(ledger.Failures, failure => failure.Code == "reconnect-event-ambiguity" && failure.Client == client);
    }

    [Fact]
    public void FailsWhenIosReconnectEventIsDuplicated()
    {
        using var fixture = LedgerFixture.Create();
        fixture.Write("ios", 99, 22, "snapshot-after-recovery", phase: "Started");

        var ledger = fixture.Aggregate();

        Assert.False(ledger.Passed);
        Assert.Contains(ledger.Failures, failure => failure.Code == "reconnect-event-ambiguity" && failure.Client == "ios");
    }

    [Theory]
    [InlineData("ios")]
    [InlineData("display")]
    [InlineData("scripted-player-a")]
    [InlineData("scripted-player-b")]
    public void FailsWhenClientFinalVersionExceedsBackend(string client)
    {
        using var fixture = LedgerFixture.Create();
        fixture.Write(client, 99, 51, "snapshot-accepted", phase: "Completed");

        var ledger = fixture.Aggregate();

        Assert.False(ledger.Passed);
        Assert.Contains(ledger.Failures, failure => failure.Code == "client-version-ahead-of-backend" && failure.Client == client);
    }

    [Fact]
    public void FailsForInvalidJsonMissingFieldNegativeVersionAndClientMismatch()
    {
        using var fixture = LedgerFixture.Create();
        fixture.WriteRaw("ios-observation-000099.json", "not-json");
        fixture.WriteRaw("display-observation-000099.json", "{\"client\":\"display\",\"event\":\"x\",\"stateVersion\":1,\"phase\":\"Started\",\"timestampUtc\":\"2026-07-28T00:00:00Z\"}");
        fixture.WriteRaw("scripted-player-a-observation-000099.json", "{\"client\":\"scripted-player-a\",\"event\":\"x\",\"stateVersion\":-1,\"phase\":\"Started\",\"questionId\":\"\",\"timestampUtc\":\"2026-07-28T00:00:00Z\"}");
        fixture.WriteRaw("scripted-player-b-observation-000099.json", "{\"client\":\"backend\",\"event\":\"x\",\"stateVersion\":1,\"phase\":\"Started\",\"questionId\":\"\",\"timestampUtc\":\"2026-07-28T00:00:00Z\"}");

        var ledger = fixture.Aggregate();

        Assert.False(ledger.Passed);
        Assert.Contains(ledger.Failures, failure => failure.Code == "invalid-observation-json" && failure.Client == "ios");
        Assert.Contains(ledger.Failures, failure => failure.Code == "invalid-observation-json" && failure.Client == "display");
        Assert.Contains(ledger.Failures, failure => failure.Code == "invalid-observation-json" && failure.Client == "scripted-player-a");
        Assert.Contains(ledger.Failures, failure => failure.Code == "observation-client-mismatch" && failure.Client == "scripted-player-b");
    }

    [Fact]
    public void RejectsUnknownObservationFieldsRatherThanIgnoringSchemaDrift()
    {
        using var fixture = LedgerFixture.Create();
        fixture.WriteRaw("backend-observation-000099.json", "{\"client\":\"backend\",\"event\":\"x\",\"stateVersion\":51,\"phase\":\"Completed\",\"questionId\":\"\",\"timestampUtc\":\"2026-07-28T00:00:00Z\",\"unexpected\":true}");

        var ledger = fixture.Aggregate();

        Assert.False(ledger.Passed);
        Assert.Contains(ledger.Failures, failure => failure.Code == "invalid-observation-json" && failure.Client == "backend");
    }

    [Fact]
    public void SortsFilesByParsedSequenceRatherThanEnumerationOrder()
    {
        using var fixture = LedgerFixture.Empty();
        fixture.Write("ios", 3, 30, "snapshot-after-recovery", phase: "Completed");
        fixture.Write("ios", 1, 10, "snapshot-before-disconnect", phase: "Started");
        fixture.Write("ios", 2, 20, "snapshot-accepted", phase: "Started");
        fixture.Write("display", 1, 10, "snapshot-before-reload", phase: "Started");
        fixture.Write("display", 2, 20, "snapshot-after-reconnect", phase: "Completed");
        fixture.Write("scripted-player-a", 1, 20, "snapshot-accepted");
        fixture.Write("scripted-player-b", 1, 20, "snapshot-accepted");
        fixture.Write("backend", 1, 10, "snapshot-accepted", phase: "Lobby");
        fixture.Write("backend", 2, 30, "snapshot-accepted", phase: "Completed");

        var ledger = fixture.Aggregate();

        Assert.True(ledger.Passed);
        Assert.Equal([10, 20, 30], ledger.Clients["ios"].Observations.Select(observation => observation.StateVersion));
    }

    [Fact]
    public void FailsOnSequenceCollisionGapAndTimestampRegression()
    {
        using var fixture = LedgerFixture.Create();
        fixture.Write("ios", 1, 10, "snapshot-before-disconnect", fileName: "ios-observation-1.json");
        fixture.Write("display", 99, 40, "snapshot-accepted", timestampUtc: "2026-07-27T00:00:00.0000000+00:00");

        var ledger = fixture.Aggregate();

        Assert.False(ledger.Passed);
        Assert.Contains(ledger.Failures, failure => failure.Code == "duplicate-observation-sequence" && failure.Client == "ios");
        Assert.Contains(ledger.Failures, failure => failure.Code == "observation-sequence-gap" && failure.Client == "display");
        Assert.Contains(ledger.Failures, failure => failure.Code == "timestamp-regression" && failure.Client == "display");
    }

    [Fact]
    public void WritesRoundTrippableLedgerWithoutTemporaryArtifact()
    {
        using var fixture = LedgerFixture.Create();
        var ledger = fixture.Aggregate();

        StateVersionLedgerAggregator.Write(fixture.Directory, ledger);

        var path = System.IO.Path.Combine(fixture.Directory, "state-version-ledger.json");
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(System.IO.Path.Combine(fixture.Directory, ".state-version-ledger.json.tmp")));
        Assert.True(JsonSerializer.Deserialize<StateVersionLedgerResult>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Passed);
    }

    private sealed class LedgerFixture : IDisposable
    {
        private LedgerFixture(string directory) => Directory = directory;
        public string Directory { get; }
        public static LedgerFixture Empty()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"partygame-ledger-tests-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            return new LedgerFixture(directory);
        }
        public static LedgerFixture Create()
        {
            var fixture = Empty();
            fixture.Write("ios", 1, 10, "snapshot-lobby-accepted", phase: "Lobby");
            fixture.Write("ios", 2, 20, "snapshot-before-disconnect");
            fixture.Write("ios", 3, 21, "snapshot-after-recovery");
            fixture.Write("ios", 4, 50, "snapshot-completed", phase: "Completed");
            fixture.Write("display", 1, 10, "snapshot-initial-attach", phase: "Lobby");
            fixture.Write("display", 2, 30, "snapshot-before-reload");
            fixture.Write("display", 3, 31, "snapshot-after-reconnect");
            fixture.Write("display", 4, 50, "snapshot-completed", phase: "Completed");
            fixture.Write("scripted-player-a", 1, 10, "attach-player-response", phase: "Lobby");
            fixture.Write("scripted-player-a", 2, 50, "room-started", phase: "Completed");
            fixture.Write("scripted-player-b", 1, 10, "attach-player-response", phase: "Lobby");
            fixture.Write("scripted-player-b", 2, 50, "room-started", phase: "Completed");
            fixture.Write("backend", 1, 10, "room-created", phase: "Lobby");
            fixture.Write("backend", 2, 20, "snapshot-accepted", phase: "Started");
            fixture.Write("backend", 3, 50, "snapshot-accepted", phase: "Completed");
            return fixture;
        }
        public StateVersionLedgerResult Aggregate() => new StateVersionLedgerAggregator().Aggregate(Directory);
        public void DeleteClient(string client)
        {
            foreach (var path in System.IO.Directory.EnumerateFiles(Directory, $"{client}-observation-*.json")) File.Delete(path);
        }
        public void RemoveEvent(string client, string @event)
        {
            foreach (var path in System.IO.Directory.EnumerateFiles(Directory, $"{client}-observation-*.json"))
                if (JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("event").GetString() == @event) File.Delete(path);
        }
        public void ReplaceReconnect(string client, string beforeEvent, long beforeVersion, string recoveredEvent, long recoveredVersion)
        {
            RemoveEvent(client, beforeEvent);
            RemoveEvent(client, recoveredEvent);
            Write(client, 90, beforeVersion, beforeEvent);
            Write(client, 91, recoveredVersion, recoveredEvent);
        }
        public void Write(string client, int sequence, long version, string @event, string phase = "Started", string questionId = "", string? timestampUtc = null, string? fileName = null)
        {
            var name = fileName ?? $"{client}-observation-{sequence:D6}.json";
            var timestamp = timestampUtc ?? $"2026-07-28T00:00:{sequence % 60:D2}.0000000+00:00";
            WriteRaw(name, JsonSerializer.Serialize(new { client, @event, stateVersion = version, phase, questionId, timestampUtc = timestamp }));
        }
        public void WriteRaw(string name, string contents) => File.WriteAllText(System.IO.Path.Combine(Directory, name), contents);
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
}
