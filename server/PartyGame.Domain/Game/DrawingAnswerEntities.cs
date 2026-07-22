namespace PartyGame.Domain.Game;

public sealed class DrawingAnswerEligiblePlayer
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid PlayerId { get; set; }
}

public sealed class DrawingAnswerSubmission
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid AuthorPlayerId { get; set; }
    public Guid MediaAssetId { get; set; }
    public Guid ClientSubmissionId { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public int? RevealOrder { get; set; }
    public MediaAsset MediaAsset { get; set; } = null!;
}

public sealed class DrawingAnswerVoteEligiblePlayer
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid PlayerId { get; set; }
}

public sealed class DrawingAnswerVote
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid VoterPlayerId { get; set; }
    public Guid SelectedDrawingAnswerId { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
}
