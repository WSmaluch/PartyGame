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

public sealed record FinalRoundPrivateState(bool HasSubmittedSelfie, Guid? AssignedArtifactId, string? SourceDisplayMediaUrl, string? SourceThumbnailMediaUrl, bool HasSubmittedEdit, bool HasSubmittedVote);
