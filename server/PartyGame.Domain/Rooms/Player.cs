namespace PartyGame.Domain.Rooms;

public sealed class Player
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public string Nickname { get; set; } = string.Empty;
    public string NormalizedNickname { get; set; } = string.Empty;
    public bool IsHost { get; set; }
    public bool IsReady { get; set; }
    public bool IsConnected { get; set; }
    public bool HasProfilePhoto { get; set; }
    public string? ProfilePhotoStorageKey { get; set; }
    public string? ProfilePhotoContentType { get; set; }
    public DateTimeOffset JoinedAtUtc { get; set; }
    public DateTimeOffset LastSeenAtUtc { get; set; }
    public int Score { get; set; }
    public GameRoom Room { get; set; } = null!;
    public PlayerSession Session { get; set; } = null!;
}
