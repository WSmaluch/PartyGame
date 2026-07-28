using System.Text.Json;

namespace PartyGame.MixedE2EOrchestrator;

public sealed record StateVersionObservation
{
    public StateVersionObservation(string client, string @event, long stateVersion, string phase, string questionId, DateTimeOffset timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(client)) throw new ArgumentException("client jest wymagany.", nameof(client));
        if (string.IsNullOrWhiteSpace(@event)) throw new ArgumentException("event jest wymagany.", nameof(@event));
        if (stateVersion < 0) throw new ArgumentOutOfRangeException(nameof(stateVersion), "stateVersion nie może być ujemny.");
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(questionId);
        if (timestampUtc.Offset != TimeSpan.Zero) throw new ArgumentException("timestampUtc musi być w UTC.", nameof(timestampUtc));

        Client = client;
        Event = @event;
        StateVersion = stateVersion;
        Phase = phase;
        QuestionId = questionId;
        TimestampUtc = timestampUtc;
    }

    public string Client { get; }
    public string Event { get; }
    public long StateVersion { get; }
    public string Phase { get; }
    public string QuestionId { get; }
    public DateTimeOffset TimestampUtc { get; }
}

public sealed class StateVersionTracker
{
    public long? LastAcceptedStateVersion { get; private set; }
    public int ObservationCount { get; private set; }
    public int RegressionCount { get; private set; }

    public bool TryAccept(long candidate)
    {
        if (candidate < 0) throw new ArgumentOutOfRangeException(nameof(candidate));
        if (LastAcceptedStateVersion is { } previous)
        {
            if (candidate < previous)
            {
                RegressionCount++;
                return false;
            }
            if (candidate == previous) return false;
        }

        LastAcceptedStateVersion = candidate;
        ObservationCount++;
        return true;
    }
}

public sealed class StateVersionObservationWriter
{
    private readonly string _client;
    private readonly string _directory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private int _sequence;

    public StateVersionObservationWriter(string client, string directory)
    {
        if (string.IsNullOrWhiteSpace(client)) throw new ArgumentException("client jest wymagany.", nameof(client));
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Brak katalogu koordynacji: {directory}");
        _client = client;
        _directory = directory;
    }

    public string Write(StateVersionObservation observation)
    {
        if (observation.Client != _client) throw new InvalidOperationException("Writer otrzymał obserwację innego klienta.");
        var sequence = checked(_sequence + 1);
        var name = $"{_client}-observation-{sequence:D6}.json";
        var target = Path.Combine(_directory, name);
        var temporary = Path.Combine(_directory, $".{name}.tmp");
        if (File.Exists(target) || File.Exists(temporary)) throw new IOException($"Kolizja pliku obserwacji: {target}");

        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(observation, _json));
            File.Move(temporary, target);
            if (!File.Exists(target)) throw new IOException($"Nie utworzono pliku obserwacji: {target}");
            _ = JsonSerializer.Deserialize<StateVersionObservation>(File.ReadAllText(target), _json)
                ?? throw new JsonException("Nie można ponownie odczytać obserwacji.");
            _sequence = sequence;
            return target;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}

public sealed class ClientStateVersionRecorder
{
    private readonly object _gate = new();
    private readonly string _client;
    private readonly StateVersionObservationWriter _writer;
    private JsonElement? _latestSnapshot;

    public ClientStateVersionRecorder(string client, string coordinationDirectory)
    {
        _client = client;
        Tracker = new StateVersionTracker();
        _writer = new StateVersionObservationWriter(client, coordinationDirectory);
    }

    public StateVersionTracker Tracker { get; }

    public bool Observe(JsonElement snapshot, string @event)
    {
        lock (_gate)
        {
            var version = snapshot.GetProperty("stateVersion").GetInt64();
            if (!Tracker.TryAccept(version))
            {
                if (Tracker.LastAcceptedStateVersion is { } accepted && version < accepted)
                    throw new InvalidOperationException($"{_client}: stateVersion cofnął się z {accepted} do {version}.");
                return false;
            }

            var phase = SnapshotPhase(snapshot);
            var questionId = SnapshotQuestionId(snapshot);
            _writer.Write(new StateVersionObservation(_client, @event, version, phase, questionId, DateTimeOffset.UtcNow));
            _latestSnapshot = snapshot.Clone();
            return true;
        }
    }

    public bool TryGetLatestSnapshot(out JsonElement snapshot)
    {
        lock (_gate)
        {
            if (_latestSnapshot is not { } latest)
            {
                snapshot = default;
                return false;
            }
            snapshot = latest.Clone();
            return true;
        }
    }

    private static string SnapshotPhase(JsonElement snapshot) =>
        snapshot.TryGetProperty("game", out var game) && game.ValueKind != JsonValueKind.Null &&
        game.TryGetProperty("stage", out var stage) && stage.ValueKind == JsonValueKind.String
            ? stage.GetString()!
            : snapshot.GetProperty("phase").GetString() ?? string.Empty;

    private static string SnapshotQuestionId(JsonElement snapshot) =>
        snapshot.TryGetProperty("game", out var game) && game.ValueKind != JsonValueKind.Null &&
        game.TryGetProperty("question", out var question) && question.ValueKind != JsonValueKind.Null &&
        question.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString() ?? string.Empty
            : string.Empty;
}
