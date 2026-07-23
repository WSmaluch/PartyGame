using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Media;

namespace PartyGame.Infrastructure.Rooms;

public interface IRoomService
{
    Task<RoomCreatedResult> CreateAsync(string? nickname, RoomSettings? settings, List<string>? selectedPackageKeys, List<string>? enabledQuestionTypes, Guid? contentPackageVersionId = null, CancellationToken cancellationToken = default);
    Task<RoomCreatedResult> JoinAsync(string roomCode, string? nickname, CancellationToken cancellationToken = default);
    Task<GameRoom> GetAsync(string roomCode, CancellationToken cancellationToken = default);
    Task<PlayerAuthorizationResult> ResumeAsync(string roomCode, Guid playerId, string? token, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> AttachPlayerAsync(string roomCode, Guid playerId, string? token, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> AttachDisplayAsync(string roomCode, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> SetReadyAsync(string roomCode, Guid playerId, string? token, bool isReady, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> SetProfilePhotoAsync(string roomCode, Guid playerId, string? token, Guid mediaAssetId, StoredMediaResult storedMedia, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> DisconnectPlayerAsync(string roomCode, Guid playerId, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> DisconnectDisplayAsync(string roomCode, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> SubmitSelectionAsync(string roomCode, Guid playerId, string? token, Guid selectedPlayerId, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> SubmitTextAnswerAsync(string roomCode, Guid playerId, string? token, string text, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> SubmitTextAnswerVoteAsync(string roomCode, Guid playerId, string? token, Guid selectedAnswerId, CancellationToken cancellationToken = default);
    Task<PhotoAnswerUploadResult> SubmitPhotoAnswerAsync(string roomCode, Guid playerId, string? token, Guid questionInstanceId, Guid clientSubmissionId, Stream content, long byteLength, string contentType, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> SubmitPhotoAnswerVoteAsync(string roomCode, Guid playerId, string? token, Guid questionInstanceId, Guid selectedAnswerId, CancellationToken cancellationToken = default);
    Task<DrawingAnswerUploadResult> SubmitDrawingAnswerAsync(string roomCode, Guid playerId, string? token, Guid questionInstanceId, Guid clientSubmissionId, Stream content, long byteLength, string contentType, CancellationToken cancellationToken = default);
    Task<RoomMutationResult> SubmitDrawingAnswerVoteAsync(string roomCode, Guid playerId, string? token, Guid questionInstanceId, Guid selectedAnswerId, CancellationToken cancellationToken = default);
    Task<PartyGame.Domain.Rooms.PlayerPrivateGameState> GetPlayerPrivateGameStateAsync(string roomCode, Guid playerId, CancellationToken cancellationToken = default);
}

public sealed record RoomCreatedResult(GameRoom Room, Player Player, string ReconnectToken);
public sealed record PlayerAuthorizationResult(GameRoom Room, Player Player);
public sealed record RoomMutationResult(GameRoom Room, bool PublicStateChanged, bool StartedNow);
public sealed record PhotoAnswerUploadResult(GameRoom Room, Guid PhotoAnswerId, bool Created);
public sealed record DrawingAnswerUploadResult(GameRoom Room, Guid DrawingAnswerId, bool Created);
