using PartyGame.Domain.Content;
using PartyGame.Domain.Game;

namespace PartyGame.Domain.Rooms;

public sealed class GameRoom
{
    public const int MinimumPlayers = 3;
    public const int MaximumPlayers = 10;

    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public RoomPhase Phase { get; set; } = RoomPhase.Lobby;
    public long StateVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? StartedAtUtc { get; set; }
    public Guid HostPlayerId { get; set; }
    public bool DisplayConnected { get; set; }
    public RoomSettings Settings { get; set; } = new();
    // Rooms created through RoomService are always pinned to one version. Nullable
    // remains for legacy/test fixtures created before package versioning.
    public Guid? ContentPackageVersionId { get; set; }
    public GamePackage? ContentPackage { get; set; }
    public List<string> SelectedPackageKeys { get; set; } = [];
    public List<QuestionType> EnabledQuestionTypes { get; set; } = new();
    public List<Player> Players { get; set; } = [];
    public GameSession? Session { get; set; }

    public void PublicStateChanged(DateTimeOffset utcNow)
    {
        StateVersion++;
        UpdatedAtUtc = utcNow;
    }
}
