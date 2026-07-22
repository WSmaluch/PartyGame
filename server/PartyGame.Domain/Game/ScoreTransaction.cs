using PartyGame.Domain.Rooms;

namespace PartyGame.Domain.Game;

public sealed class ScoreTransaction
{
    public Guid Id { get; set; }
    public Guid GameSessionId { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid PlayerId { get; set; }
    public int Points { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }

    public GameSession Session { get; set; } = null!;
    public GameQuestionInstance QuestionInstance { get; set; } = null!;
    public Player Player { get; set; } = null!;
}
