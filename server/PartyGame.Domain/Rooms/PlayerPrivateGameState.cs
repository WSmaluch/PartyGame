using System;

namespace PartyGame.Domain.Rooms;

public sealed record PlayerPrivateGameState(
    Guid PlayerId,
    Guid? QuestionInstanceId,
    bool HasSubmittedTextAnswer,
    Guid? OwnTextAnswerId,
    bool HasSubmittedTextAnswerVote,
    bool IsEligibleForTextAnswerVote = false,
    bool HasSubmittedPhotoAnswer = false,
    Guid? OwnPhotoAnswerId = null,
    bool HasSubmittedPhotoAnswerVote = false,
    bool HasSubmittedDrawingAnswer = false,
    Guid? OwnDrawingAnswerId = null,
    bool HasSubmittedDrawingAnswerVote = false,
    bool IsEligibleForDrawingAnswer = false,
    FinalRoundPrivateState? FinalRound = null
);

/// <summary>
/// The part of Final Round that is actionable by exactly one player.  Unlike the
/// public final-round snapshot this is safe to send to that player only and is
/// deliberately self-sufficient: a client must not infer its selfie task from
/// another player's public artifact.
/// </summary>
public sealed record FinalRoundPrivateState(
    bool HasSubmittedSelfie,
    Guid? AssignedArtifactId,
    string? SourceDisplayMediaUrl,
    string? SourceThumbnailMediaUrl,
    bool HasSubmittedEdit,
    bool HasSubmittedVote,
    FinalRoundPrivateText? SelfiePrompt = null,
    FinalRoundPrivateText? TargetRole = null,
    bool CanSubmitSelfie = false);

public sealed record FinalRoundPrivateText(string Pl, string En);
