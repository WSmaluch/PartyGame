namespace PartyGame.GameEngine;

public interface IGameClock
{
    DateTimeOffset UtcNow { get; }
}
