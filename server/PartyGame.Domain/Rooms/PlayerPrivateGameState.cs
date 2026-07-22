using System;

namespace PartyGame.Domain.Rooms;

public sealed record PlayerPrivateGameState(
    Guid PlayerId,
    Guid? QuestionInstanceId,
    bool HasSubmittedTextAnswer,
    Guid? OwnTextAnswerId,
    bool HasSubmittedTextAnswerVote,
    bool HasSubmittedPhotoAnswer = false,
    Guid? OwnPhotoAnswerId = null,
    bool HasSubmittedPhotoAnswerVote = false,
    bool HasSubmittedDrawingAnswer = false,
    Guid? OwnDrawingAnswerId = null,
    bool HasSubmittedDrawingAnswerVote = false
);
