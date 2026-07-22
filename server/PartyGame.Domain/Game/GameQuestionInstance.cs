using PartyGame.Domain.Content;

namespace PartyGame.Domain.Game;

public sealed class GameQuestionInstance
{
    public Guid Id { get; set; }
    public Guid RoundId { get; set; }
    public Guid QuestionId { get; set; }
    public int QuestionNumber { get; set; }
    public GameStage Stage { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? AnsweringStartedAtUtc { get; set; }
    public DateTimeOffset? AnsweringEndsAtUtc { get; set; }
    public DateTimeOffset? ResultsStartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public Guid? SubjectPlayerId { get; set; }

    public GameRound Round { get; set; } = null!;
    public GameQuestion Question { get; set; } = null!;

    public List<GameQuestionEligiblePlayer> EligiblePlayers { get; set; } = [];
    public List<PlayerSelectionAnswer> Answers { get; set; } = [];

    public List<TextAnswerEligiblePlayer> TextAnswerEligiblePlayers { get; set; } = [];
    public List<TextAnswerSubmission> TextAnswerSubmissions { get; set; } = [];
    public List<TextAnswerVoteEligiblePlayer> TextAnswerVoteEligiblePlayers { get; set; } = [];
    public List<TextAnswerVote> TextAnswerVotes { get; set; } = [];

    public List<PhotoAnswerEligiblePlayer> PhotoAnswerEligiblePlayers { get; set; } = [];
    public List<PhotoAnswerSubmission> PhotoAnswerSubmissions { get; set; } = [];
    public List<PhotoAnswerVoteEligiblePlayer> PhotoAnswerVoteEligiblePlayers { get; set; } = [];
    public List<PhotoAnswerVote> PhotoAnswerVotes { get; set; } = [];

    public List<DrawingAnswerEligiblePlayer> DrawingAnswerEligiblePlayers { get; set; } = [];
    public List<DrawingAnswerSubmission> DrawingAnswerSubmissions { get; set; } = [];
    public List<DrawingAnswerVoteEligiblePlayer> DrawingAnswerVoteEligiblePlayers { get; set; } = [];
    public List<DrawingAnswerVote> DrawingAnswerVotes { get; set; } = [];
}
