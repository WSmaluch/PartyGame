using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PartyGame.Api.Diagnostics;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Game;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Api.Hubs;

public sealed class GameHub : Hub
{
    private readonly IGameClock clock;
    private readonly IRoomService roomService;
    private readonly IRoomConnectionRegistry connectionRegistry;
    private readonly ILogger<GameHub> logger;

    [ActivatorUtilitiesConstructor]
    public GameHub(
        IGameClock clock,
        IRoomService roomService,
        IRoomConnectionRegistry connectionRegistry,
        ILogger<GameHub> logger)
    {
        this.clock = clock;
        this.roomService = roomService;
        this.connectionRegistry = connectionRegistry;
        this.logger = logger;
    }

    public GameHub(IGameClock clock)
        : this(clock, null!, null!, NullLogger<GameHub>.Instance)
    {
    }

    public HubPingResponse Ping() => new("pong", clock.UtcNow);

    public override Task OnConnectedAsync()
    {
        logger.LogInformation("SignalR connection opened {ConnectionId} correlation {CorrelationId}", Context.ConnectionId, CorrelationId.ForHub(Context));
        return base.OnConnectedAsync();
    }

    public async Task<RoomSnapshot> AttachPlayer(string roomCode, Guid playerId, string reconnectToken)
    {
        try
        {
            var code = roomCode.Trim().ToUpperInvariant();
            if (!connectionRegistry.CanAttachPlayer(Context.ConnectionId, code, playerId))
                throw new RoomConflictException("A SignalR connection cannot change its player identity after attach.");
            await roomService.ResumeAsync(code, playerId, reconnectToken, Context.ConnectionAborted);
            var previousConnection = connectionRegistry.AttachPlayer(Context.ConnectionId, code, playerId);
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomNotifier.GroupName(code), Context.ConnectionAborted);
            if (previousConnection is not null)
            {
                await Groups.RemoveFromGroupAsync(previousConnection, RoomNotifier.GroupName(code));
            }
            var result = await roomService.AttachPlayerAsync(code, playerId, reconnectToken, Context.ConnectionAborted);
            logger.LogInformation("SignalR command accepted {EventName} {ConnectionId} {ConnectionRole} {RoomCode} {PlayerId} {CorrelationId}", "AttachPlayer", Context.ConnectionId, "player", code, playerId, CorrelationId.ForHub(Context));
            await NotifyAsync(result);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(code, playerId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("PlayerPrivateGameStateUpdated", privateState, Context.ConnectionAborted);
            return result.Room.ToSnapshot();
        }
        catch (RoomException exception)
        {
            logger.LogWarning("SignalR command rejected {EventName} {ConnectionId} {ErrorCode} {CorrelationId}", "AttachPlayer", Context.ConnectionId, "AUTH_INVALID", CorrelationId.ForHub(Context));
            throw new HubException(exception.Message);
        }
    }

    public async Task<RoomSnapshot> AttachDisplay(string roomCode)
    {
        try
        {
            var room = await roomService.GetAsync(roomCode, Context.ConnectionAborted);
            var code = room.Code;
            if (!connectionRegistry.CanAttachDisplay(Context.ConnectionId, code))
                throw new RoomConflictException("A SignalR connection cannot change its display room after attach.");
            var previousConnection = connectionRegistry.AttachDisplay(Context.ConnectionId, code);
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomNotifier.GroupName(code), Context.ConnectionAborted);
            if (previousConnection is not null)
            {
                await Clients.Client(previousConnection).SendAsync("DisplayReplaced", Context.ConnectionAborted);
                await Groups.RemoveFromGroupAsync(previousConnection, RoomNotifier.GroupName(code));
            }
            var result = await roomService.AttachDisplayAsync(code, Context.ConnectionAborted);
            logger.LogInformation("SignalR command accepted {EventName} {ConnectionId} {ConnectionRole} {RoomCode} {CorrelationId}", "AttachDisplay", Context.ConnectionId, "display", code, CorrelationId.ForHub(Context));
            await NotifyAsync(result);
            return result.Room.ToSnapshot();
        }
        catch (RoomException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public async Task<RoomSnapshot> DetachDisplay(string roomCode)
    {
        try
        {
            var code = roomCode.Trim().ToUpperInvariant();
            if (!connectionRegistry.IsActiveDisplay(Context.ConnectionId, code))
                throw new RoomConflictException("Only the active display connection can detach from this room.");

            var result = await roomService.DisconnectDisplayAsync(code, Context.ConnectionAborted);
            connectionRegistry.RemoveIfActive(Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, RoomNotifier.GroupName(code), Context.ConnectionAborted);
            logger.LogInformation("SignalR command accepted {EventName} {ConnectionId} {ConnectionRole} {RoomCode} {CorrelationId}", "DetachDisplay", Context.ConnectionId, "display", code, CorrelationId.ForHub(Context));
            await NotifyAsync(result);
            return result.Room.ToSnapshot();
        }
        catch (RoomException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public async Task<RoomSnapshot> SetReady(string roomCode, Guid playerId, string reconnectToken, bool isReady)
    {
        try
        {
            if (!connectionRegistry.IsActivePlayer(Context.ConnectionId, roomCode.Trim(), playerId))
            {
                throw new RoomConflictException("AttachPlayer must establish the active player connection before Ready can change.");
            }
            var result = await roomService.SetReadyAsync(roomCode, playerId, reconnectToken, isReady, Context.ConnectionAborted);
            logger.LogInformation("Player {PlayerId} changed Ready to {IsReady} in room {RoomCode}", playerId, isReady, result.Room.Code);
            await NotifyAsync(result);
            return result.Room.ToSnapshot();
        }
        catch (RoomException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public async Task<RoomSnapshot> GetRoomSnapshot(string roomCode)
    {
        try
        {
            var code = roomCode.Trim().ToUpperInvariant();
            if (!connectionRegistry.HasRoomAccess(Context.ConnectionId, code))
                throw new RoomConflictException("AttachPlayer or AttachDisplay must establish room access before requesting a snapshot.");
            return (await roomService.GetAsync(code, Context.ConnectionAborted)).ToSnapshot();
        }
        catch (RoomException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public async Task SubmitPlayerSelectionWithSubmission(string roomCode, Guid playerId, string reconnectToken, Guid selectedPlayerId, Guid questionInstanceId, Guid clientSubmissionId)
    {
        try
        {
            if (!connectionRegistry.IsActivePlayer(Context.ConnectionId, roomCode.Trim(), playerId))
            {
                throw new RoomConflictException("Not an active player.");
            }
            var result = await roomService.SubmitSelectionAsync(
                roomCode,
                playerId,
                reconnectToken,
                selectedPlayerId,
                questionInstanceId,
                clientSubmissionId,
                Context.ConnectionAborted);

            await NotifyAsync(result);
        }
        catch (RoomException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public Task SubmitPlayerSelection(string roomCode, Guid playerId, string reconnectToken, Guid selectedPlayerId) =>
        SubmitPlayerSelectionWithSubmission(roomCode, playerId, reconnectToken, selectedPlayerId, Guid.Empty, Guid.Empty);

    public async Task SubmitTextAnswerWithSubmission(string roomCode, Guid playerId, string reconnectToken, string text, Guid questionInstanceId, Guid clientSubmissionId)
    {
        try
        {
            if (!connectionRegistry.IsActivePlayer(Context.ConnectionId, roomCode.Trim(), playerId))
            {
                throw new RoomConflictException("Not an active player.");
            }
            var result = await roomService.SubmitTextAnswerAsync(
                roomCode,
                playerId,
                reconnectToken,
                text,
                questionInstanceId,
                clientSubmissionId,
                Context.ConnectionAborted);

            await NotifyAsync(result);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(roomCode, playerId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("PlayerPrivateGameStateUpdated", privateState, Context.ConnectionAborted);
        }
        catch (RoomException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public Task SubmitTextAnswer(string roomCode, Guid playerId, string reconnectToken, string text) =>
        SubmitTextAnswerWithSubmission(roomCode, playerId, reconnectToken, text, Guid.Empty, Guid.Empty);

    public async Task SubmitTextAnswerVoteWithSubmission(string roomCode, Guid playerId, string reconnectToken, Guid selectedAnswerId, Guid questionInstanceId, Guid clientSubmissionId)
    {
        try
        {
            if (!connectionRegistry.IsActivePlayer(Context.ConnectionId, roomCode.Trim(), playerId))
            {
                throw new RoomConflictException("Not an active player.");
            }
            var result = await roomService.SubmitTextAnswerVoteAsync(
                roomCode,
                playerId,
                reconnectToken,
                selectedAnswerId,
                questionInstanceId,
                clientSubmissionId,
                Context.ConnectionAborted);

            await NotifyAsync(result);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(roomCode, playerId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("PlayerPrivateGameStateUpdated", privateState, Context.ConnectionAborted);
        }
        catch (RoomException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public Task SubmitTextAnswerVote(string roomCode, Guid playerId, string reconnectToken, Guid selectedAnswerId) =>
        SubmitTextAnswerVoteWithSubmission(roomCode, playerId, reconnectToken, selectedAnswerId, Guid.Empty, Guid.Empty);

    public async Task SubmitPhotoAnswerVoteWithSubmission(string roomCode, Guid playerId, string reconnectToken, Guid questionInstanceId, Guid photoAnswerId, Guid clientSubmissionId)
    {
        try
        {
            if (!connectionRegistry.IsActivePlayer(Context.ConnectionId, roomCode.Trim(), playerId))
                throw new PhotoAnswerException("photo_answer_vote_player_not_eligible", "Not an active player.");
            var result = await roomService.SubmitPhotoAnswerVoteAsync(roomCode, playerId, reconnectToken, questionInstanceId, photoAnswerId, clientSubmissionId, Context.ConnectionAborted);
            await NotifyAsync(result);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(roomCode, playerId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("PlayerPrivateGameStateUpdated", privateState, Context.ConnectionAborted);
        }
        catch (RoomException exception)
        {
            throw new HubException(exception is PhotoAnswerException photo ? photo.Code : exception.Message);
        }
    }

    public Task SubmitPhotoAnswerVote(string roomCode, Guid playerId, string reconnectToken, Guid questionInstanceId, Guid photoAnswerId) =>
        SubmitPhotoAnswerVoteWithSubmission(roomCode, playerId, reconnectToken, questionInstanceId, photoAnswerId, Guid.Empty);

    public async Task SubmitDrawingAnswerVoteWithSubmission(string roomCode, Guid playerId, string reconnectToken, Guid questionInstanceId, Guid drawingAnswerId, Guid clientSubmissionId)
    {
        try { if (!connectionRegistry.IsActivePlayer(Context.ConnectionId, roomCode.Trim(), playerId)) throw new DrawingAnswerException("drawing_answer_vote_player_not_eligible", "Not an active player."); var result = await roomService.SubmitDrawingAnswerVoteAsync(roomCode, playerId, reconnectToken, questionInstanceId, drawingAnswerId, clientSubmissionId, Context.ConnectionAborted); await NotifyAsync(result); var state = await roomService.GetPlayerPrivateGameStateAsync(roomCode, playerId, Context.ConnectionAborted); await Clients.Caller.SendAsync("PlayerPrivateGameStateUpdated", state, Context.ConnectionAborted); }
        catch (RoomException exception) { throw new HubException(exception is DrawingAnswerException drawing ? drawing.Code : exception.Message); }
    }

    public Task SubmitDrawingAnswerVote(string roomCode, Guid playerId, string reconnectToken, Guid questionInstanceId, Guid drawingAnswerId) =>
        SubmitDrawingAnswerVoteWithSubmission(roomCode, playerId, reconnectToken, questionInstanceId, drawingAnswerId, Guid.Empty);

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var assignment = connectionRegistry.RemoveIfActive(Context.ConnectionId);
        if (assignment is not null)
        {
            try
            {
                var result = assignment.Role == ConnectionRole.Player
                    ? await roomService.DisconnectPlayerAsync(assignment.RoomCode, assignment.PlayerId!.Value)
                    : await roomService.DisconnectDisplayAsync(assignment.RoomCode);
                if (assignment.Role == ConnectionRole.Display)
                {
                    logger.LogInformation("SignalR connection closed {ConnectionId} {ConnectionRole} {RoomCode} {CorrelationId}", Context.ConnectionId, "display", assignment.RoomCode, CorrelationId.ForHub(Context));
                }
                await NotifyAsync(result);
            }
            catch (RoomException roomException)
            {
                logger.LogWarning(roomException, "Could not update room after SignalR disconnection");
            }
        }
        else
        {
            logger.LogInformation("SignalR connection closed {ConnectionId} {CorrelationId}", Context.ConnectionId, CorrelationId.ForHub(Context));
        }
        await base.OnDisconnectedAsync(exception);
    }

    private async Task NotifyAsync(RoomMutationResult result)
    {
        if (!result.PublicStateChanged)
        {
            return;
        }
        var snapshot = result.Room.ToSnapshot();
        if (result.StartedNow)
        {
            await Clients.Group(RoomNotifier.GroupName(result.Room.Code)).SendAsync("RoomStarted", snapshot, Context.ConnectionAborted);
        }
        await Clients.Group(RoomNotifier.GroupName(result.Room.Code)).SendAsync("RoomSnapshotUpdated", snapshot, Context.ConnectionAborted);
        await NotifyFinalRoundPrivateStatesAsync(result.Room);
    }

    private async Task NotifyFinalRoundPrivateStatesAsync(PartyGame.Domain.Rooms.GameRoom room)
    {
        if (room.Session?.Stage is not (GameStage.CollectingFinalSelfies or GameStage.CollectingFinalEdits or GameStage.CollectingFinalVotes))
            return;

        foreach (var player in room.Players)
        {
            var connectionId = connectionRegistry.GetActivePlayerConnection(player.Id);
            if (connectionId is null)
                continue;
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(room.Code, player.Id, Context.ConnectionAborted);
            await Clients.Client(connectionId).SendAsync("PlayerPrivateGameStateUpdated", privateState, Context.ConnectionAborted);
        }
    }
}

public sealed record HubPingResponse(string Status, DateTimeOffset UtcTime);
