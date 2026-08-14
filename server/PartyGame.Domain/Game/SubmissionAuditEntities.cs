namespace PartyGame.Domain.Game;

public enum SubmissionActionType
{
    PlayerSelection,
    TextAnswer,
    TextAnswerVote,
    PhotoAnswer,
    PhotoAnswerVote,
    DrawingAnswer,
    DrawingAnswerVote,
    FinalSelfie,
    FinalEdit,
    FinalVote
}

public enum SubmissionAuditResult
{
    Accepted,
    IdempotentReplay,
    Conflict
}

// A receipt is the durable concurrency guard. Audit entries are append-only so
// diagnostics can distinguish the original mutation from retried transport calls.
public sealed class SubmissionReceipt
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public SubmissionActionType ActionType { get; set; }
    public Guid ClientSubmissionId { get; set; }
    public string PayloadFingerprint { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class SubmissionAuditEntry
{
    public Guid Id { get; set; }
    public Guid RoomId { get; set; }
    public Guid PlayerId { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public SubmissionActionType ActionType { get; set; }
    public Guid ClientSubmissionId { get; set; }
    public string PayloadFingerprint { get; set; } = string.Empty;
    public SubmissionAuditResult Result { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
