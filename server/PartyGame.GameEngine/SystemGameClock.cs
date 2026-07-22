namespace PartyGame.GameEngine;

public sealed class SystemGameClock : IGameClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
