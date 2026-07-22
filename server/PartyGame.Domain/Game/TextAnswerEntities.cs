using System;

namespace PartyGame.Domain.Game;

public sealed class TextAnswerEligiblePlayer
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid PlayerId { get; set; }
}

public sealed class TextAnswerSubmission
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid AuthorPlayerId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAtUtc { get; set; }
    public int? RevealOrder { get; set; }
}

public sealed class TextAnswerVoteEligiblePlayer
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid PlayerId { get; set; }
}

public sealed class TextAnswerVote
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid VoterPlayerId { get; set; }
    public Guid SelectedTextAnswerId { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
}
