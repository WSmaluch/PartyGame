namespace PartyGame.Domain.Rooms;

public sealed class PlayerSession
{
    public Guid PlayerId { get; set; }
    public string ReconnectTokenHash { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public Player Player { get; set; } = null!;
}
