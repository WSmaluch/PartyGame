using System.Globalization;
using System.Text.Json;

namespace PartyGame.MixedE2EOrchestrator;

public sealed record StateVersionLedgerFailure(string Code, string? Client, string Detail);

public sealed record ClientStateVersionLedger(
    int ObservationCount,
    int RegressionCount,
    long? FirstVersion,
    long? LastVersion,
    long? MinimumVersion,
    long? MaximumVersion,
    DateTimeOffset? FirstTimestampUtc,
    DateTimeOffset? LastTimestampUtc,
    IReadOnlyList<string> Events,
    IReadOnlyList<StateVersionObservation> Observations,
    bool Passed,
    IReadOnlyList<StateVersionLedgerFailure> Failures,
    long? VersionBeforeDisconnect = null,
    long? RecoveredVersion = null,
    bool? RecoveryVersionPassed = null,
    int ReconnectEventCount = 0);

public sealed record StateVersionLedgerResult(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    bool Passed,
    int FailureCount,
    IReadOnlyList<StateVersionLedgerFailure> Failures,
    long? FinalBackendStateVersion,
    IReadOnlyDictionary<string, ClientStateVersionLedger> Clients);

public sealed class StateVersionLedgerAggregator
{
    public static readonly IReadOnlyList<string> RequiredClients = ["ios", "display", "scripted-player-a", "scripted-player-b", "backend"];
    private static readonly HashSet<string> ObservationFields = ["client", "event", "stateVersion", "phase", "questionId", "timestampUtc"];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public StateVersionLedgerResult Aggregate(string coordinationDirectory)
    {
        if (!Directory.Exists(coordinationDirectory)) throw new DirectoryNotFoundException($"Brak katalogu koordynacji: {coordinationDirectory}");

        var allFailures = new List<StateVersionLedgerFailure>();
        var ledgers = new SortedDictionary<string, ClientStateVersionLedger>(StringComparer.Ordinal);
        foreach (var client in RequiredClients)
        {
            var ledger = ReadClient(coordinationDirectory, client);
            ledgers.Add(client, ledger);
            allFailures.AddRange(ledger.Failures);
        }

        ApplyReconnectRules(ledgers["ios"], "ios", "snapshot-before-disconnect", "snapshot-after-recovery", allFailures);
        ApplyReconnectRules(ledgers["display"], "display", "snapshot-before-reload", "snapshot-after-reconnect", allFailures);

        var backendFinalVersion = ledgers["backend"].LastVersion;
        if (backendFinalVersion is null)
        {
            allFailures.Add(new("missing-backend-final-version", "backend", "Backend nie ma zaakceptowanej obserwacji końcowej."));
        }
        else
        {
            foreach (var client in RequiredClients.Where(client => client != "backend"))
            {
                if (ledgers[client].LastVersion is { } version && version > backendFinalVersion)
                    allFailures.Add(new("client-version-ahead-of-backend", client, $"Wersja {version} jest większa od backend {backendFinalVersion}."));
            }
        }

        var finalLedgers = new SortedDictionary<string, ClientStateVersionLedger>(StringComparer.Ordinal);
        foreach (var client in RequiredClients)
        {
            var source = ledgers[client];
            var failures = allFailures.Where(failure => failure.Client == client).ToArray();
            var reconnect = ReconnectFields(source, client);
            finalLedgers.Add(client, source with { Passed = failures.Length == 0, Failures = failures, VersionBeforeDisconnect = reconnect.Before, RecoveredVersion = reconnect.Recovered, RecoveryVersionPassed = reconnect.Passed, ReconnectEventCount = reconnect.Count });
        }

        return new StateVersionLedgerResult(1, DateTimeOffset.UtcNow, allFailures.Count == 0, allFailures.Count, allFailures.OrderBy(failure => failure.Client, StringComparer.Ordinal).ThenBy(failure => failure.Code, StringComparer.Ordinal).ToArray(), backendFinalVersion, finalLedgers);
    }

    public static void Write(string coordinationDirectory, StateVersionLedgerResult ledger)
    {
        var target = Path.Combine(coordinationDirectory, "state-version-ledger.json");
        var temporary = Path.Combine(coordinationDirectory, ".state-version-ledger.json.tmp");
        if (File.Exists(target) || File.Exists(temporary)) throw new IOException("Kolizja pliku state-version-ledger.json.");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(ledger, Json));
            File.Move(temporary, target);
            _ = JsonSerializer.Deserialize<StateVersionLedgerResult>(File.ReadAllText(target), Json)
                ?? throw new JsonException("Nie można ponownie odczytać ledgeru.");
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static ClientStateVersionLedger ReadClient(string directory, string client)
    {
        var failures = new List<StateVersionLedgerFailure>();
        var numberedFiles = new Dictionary<int, string>();
        var prefix = $"{client}-observation-";
        foreach (var path in Directory.EnumerateFiles(directory, $"{client}-observation-*.json"))
        {
            var name = Path.GetFileName(path);
            if (!TryParseSequence(name, client, out var sequence))
            {
                failures.Add(new("invalid-observation-file-name", client, name));
                continue;
            }
            if (!numberedFiles.TryAdd(sequence, path)) failures.Add(new("duplicate-observation-sequence", client, sequence.ToString(CultureInfo.InvariantCulture)));
        }

        if (numberedFiles.Count == 0) failures.Add(new("missing-client-observations", client, $"Brak plików {prefix}*.json."));
        var ordered = numberedFiles.OrderBy(pair => pair.Key).ToArray();
        for (var expected = 1; expected <= ordered.Length; expected++)
            if (ordered.Length >= expected && ordered[expected - 1].Key != expected)
                failures.Add(new("observation-sequence-gap", client, $"Oczekiwano {expected}, otrzymano {ordered[expected - 1].Key}."));

        var observations = new List<StateVersionObservation>();
        foreach (var (_, path) in ordered)
        {
            try
            {
                var observation = ReadObservation(path);
                if (observation.Client != client) failures.Add(new("observation-client-mismatch", client, Path.GetFileName(path)));
                else observations.Add(observation);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or ArgumentOutOfRangeException)
            {
                failures.Add(new("invalid-observation-json", client, $"{Path.GetFileName(path)}: {exception.Message}"));
            }
        }

        var regressionCount = 0;
        for (var index = 1; index < observations.Count; index++)
        {
            if (observations[index].StateVersion < observations[index - 1].StateVersion)
            {
                regressionCount++;
                failures.Add(new("state-version-regression", client, $"{observations[index - 1].StateVersion} -> {observations[index].StateVersion}"));
            }
            if (observations[index].TimestampUtc < observations[index - 1].TimestampUtc)
                failures.Add(new("timestamp-regression", client, $"{observations[index - 1].TimestampUtc:O} -> {observations[index].TimestampUtc:O}"));
        }

        return new ClientStateVersionLedger(
            observations.Count,
            regressionCount,
            observations.FirstOrDefault()?.StateVersion,
            observations.LastOrDefault()?.StateVersion,
            observations.Count == 0 ? null : observations.Min(observation => observation.StateVersion),
            observations.Count == 0 ? null : observations.Max(observation => observation.StateVersion),
            observations.FirstOrDefault()?.TimestampUtc,
            observations.LastOrDefault()?.TimestampUtc,
            observations.Select(observation => observation.Event).ToArray(),
            observations,
            failures.Count == 0,
            failures);
    }

    private static StateVersionObservation ReadObservation(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(property => !ObservationFields.Contains(property.Name)))
            throw new JsonException("Obserwacja zawiera nieznane pola albo nie jest obiektem JSON.");
        var client = RequiredString(root, "client");
        var @event = RequiredString(root, "event");
        var stateVersion = root.TryGetProperty("stateVersion", out var version) && version.ValueKind == JsonValueKind.Number && version.TryGetInt64(out var value)
            ? value : throw new JsonException("Brak liczbowego stateVersion.");
        var phase = RequiredNonNullString(root, "phase");
        var questionId = RequiredNonNullString(root, "questionId");
        var timestampText = RequiredString(root, "timestampUtc");
        if (!DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp) || timestamp.Offset != TimeSpan.Zero)
            throw new JsonException("timestampUtc musi być poprawnym czasem UTC.");
        return new StateVersionObservation(client, @event, stateVersion, phase, questionId, timestamp);
    }

    private static string RequiredString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()! : throw new JsonException($"Brak wymaganego pola {name}.");

    private static string RequiredNonNullString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? throw new JsonException($"Brak wymaganego pola {name}.") : throw new JsonException($"Brak wymaganego pola {name}.");

    private static bool TryParseSequence(string name, string client, out int sequence)
    {
        var prefix = $"{client}-observation-";
        sequence = 0;
        return name.StartsWith(prefix, StringComparison.Ordinal) && name.EndsWith(".json", StringComparison.Ordinal) &&
               int.TryParse(name[prefix.Length..^5], NumberStyles.None, CultureInfo.InvariantCulture, out sequence) && sequence > 0;
    }

    private static void ApplyReconnectRules(ClientStateVersionLedger ledger, string client, string beforeEvent, string recoveredEvent, List<StateVersionLedgerFailure> failures)
    {
        var before = ledger.Observations.Where(observation => observation.Event == beforeEvent).ToArray();
        var recovered = ledger.Observations.Where(observation => observation.Event == recoveredEvent).ToArray();
        if (before.Length != 1 || recovered.Length != 1)
        {
            failures.Add(new("reconnect-event-ambiguity", client, $"{beforeEvent}={before.Length}, {recoveredEvent}={recovered.Length}"));
            return;
        }
        if (recovered[0].StateVersion < before[0].StateVersion)
            failures.Add(new("recovery-version-regression", client, $"{before[0].StateVersion} -> {recovered[0].StateVersion}"));
    }

    private static (long? Before, long? Recovered, bool? Passed, int Count) ReconnectFields(ClientStateVersionLedger ledger, string client)
    {
        var beforeEvent = client == "ios" ? "snapshot-before-disconnect" : client == "display" ? "snapshot-before-reload" : null;
        var recoveredEvent = client == "ios" ? "snapshot-after-recovery" : client == "display" ? "snapshot-after-reconnect" : null;
        if (beforeEvent is null || recoveredEvent is null) return (null, null, null, 0);
        var before = ledger.Observations.Where(observation => observation.Event == beforeEvent).ToArray();
        var recovered = ledger.Observations.Where(observation => observation.Event == recoveredEvent).ToArray();
        var count = Math.Max(before.Length, recovered.Length);
        return before.Length == 1 && recovered.Length == 1
            ? (before[0].StateVersion, recovered[0].StateVersion, recovered[0].StateVersion >= before[0].StateVersion, count)
            : (before.Length == 1 ? before[0].StateVersion : null, recovered.Length == 1 ? recovered[0].StateVersion : null, false, count);
    }
}
