using PartyGame.Domain.Content;

namespace PartyGame.Domain.Game;

public sealed class GameRound
{
    public Guid Id { get; set; }
    public Guid GameSessionId { get; set; }
    public int RoundNumber { get; set; }
    public Guid CategoryId { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public GameSession Session { get; set; } = null!;
    public GameCategory Category { get; set; } = null!;
    public List<GameQuestionInstance> Questions { get; set; } = [];
}
