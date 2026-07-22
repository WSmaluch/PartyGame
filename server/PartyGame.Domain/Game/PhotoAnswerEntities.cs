namespace PartyGame.Domain.Game;

public sealed class MediaAsset
{
    public Guid Id { get; set; }
    public string StorageProvider { get; set; } = "Local";
    public string DisplayStorageKey { get; set; } = string.Empty;
    public string ThumbnailStorageKey { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public int Width { get; set; }
    public int Height { get; set; }
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class PhotoAnswerEligiblePlayer
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid PlayerId { get; set; }
}

public sealed class PhotoAnswerSubmission
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

public sealed class PhotoAnswerVoteEligiblePlayer
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid PlayerId { get; set; }
}

public sealed class PhotoAnswerVote
{
    public Guid Id { get; set; }
    public Guid QuestionInstanceId { get; set; }
    public Guid VoterPlayerId { get; set; }
    public Guid SelectedPhotoAnswerId { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; }
}
