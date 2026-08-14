using System.Text.Json;

namespace PartyGame.Domain.Game;

public sealed class FinalRoundState
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int CurrentPass { get; set; }
    public int TotalPasses { get; set; }
    public bool ScoresApplied { get; set; }
    public List<FinalRoundArtifact> Artifacts { get; set; } = [];
    public List<FinalRoundEdit> Edits { get; set; } = [];
    public List<FinalRoundVote> Votes { get; set; } = [];

    public static FinalRoundState? Read(string? json) => string.IsNullOrWhiteSpace(json)
        ? null
        : JsonSerializer.Deserialize<FinalRoundState>(json);

    public string Write() => JsonSerializer.Serialize(this);
}

public sealed class FinalRoundArtifact
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubjectPlayerId { get; set; }
    public string SelfiePromptPl { get; set; } = string.Empty;
    public string SelfiePromptEn { get; set; } = string.Empty;
    public string TargetRolePl { get; set; } = string.Empty;
    public string TargetRoleEn { get; set; } = string.Empty;
    public Guid? OriginalMediaAssetId { get; set; }
    public Guid? FinalMediaAssetId { get; set; }
    public Guid? SelfieClientSubmissionId { get; set; }
}

public sealed class FinalRoundEdit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArtifactId { get; set; }
    public int PassNumber { get; set; }
    public Guid EditorPlayerId { get; set; }
    public Guid? MediaAssetId { get; set; }
    public Guid? ClientSubmissionId { get; set; }
}

public sealed class FinalRoundVote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid VoterPlayerId { get; set; }
    public Guid ArtifactId { get; set; }
    public Guid? ClientSubmissionId { get; set; }
}
