using PartyGame.Domain.Rooms;

namespace PartyGame.Domain.Game;

public sealed class GameSession
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public GameStage Stage { get; set; }
    public int CurrentRoundNumber { get; set; }
    public int TotalRounds { get; set; }
    public int CurrentQuestionNumber { get; set; }
    public int QuestionsInCurrentRound { get; set; }
    public Guid? CurrentCategoryId { get; set; }
    public Guid? CurrentQuestionInstanceId { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset StageStartedAtUtc { get; set; }
    public DateTimeOffset? StageEndsAtUtc { get; set; }

    public DateTimeOffset? PausedAtUtc { get; set; }
    public GameStage? PausedStage { get; set; }
    public double? PausedRemainingMilliseconds { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }

    public GameRoom Room { get; set; } = null!;
    public List<GameRound> Rounds { get; set; } = [];
    public List<ScoreTransaction> ScoreTransactions { get; set; } = [];
}
