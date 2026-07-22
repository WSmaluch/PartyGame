using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PartyGame.Api.Contracts;
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

    public async Task<RoomSnapshot> AttachPlayer(string roomCode, Guid playerId, string reconnectToken)
    {
        try
        {
            await roomService.ResumeAsync(roomCode, playerId, reconnectToken, Context.ConnectionAborted);
            var code = roomCode.Trim().ToUpperInvariant();
            var previousConnection = connectionRegistry.AttachPlayer(Context.ConnectionId, code, playerId);
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomNotifier.GroupName(code), Context.ConnectionAborted);
            if (previousConnection is not null)
            {
                await Groups.RemoveFromGroupAsync(previousConnection, RoomNotifier.GroupName(code));
            }
            var result = await roomService.AttachPlayerAsync(code, playerId, reconnectToken, Context.ConnectionAborted);
            await NotifyAsync(result);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(code, playerId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("PlayerPrivateGameStateUpdated", privateState, Context.ConnectionAborted);
            return result.Room.ToSnapshot();
        }
        catch (RoomException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public async Task<RoomSnapshot> AttachDisplay(string roomCode)
    {
        try
        {
            var room = await roomService.GetAsync(roomCode, Context.ConnectionAborted);
            var code = room.Code;
            var previousConnection = connectionRegistry.AttachDisplay(Context.ConnectionId, code);
            await Groups.AddToGroupAsync(Context.ConnectionId, RoomNotifier.GroupName(code), Context.ConnectionAborted);
            if (previousConnection is not null)
            {
                await Clients.Client(previousConnection).SendAsync("DisplayReplaced", Context.ConnectionAborted);
                await Groups.RemoveFromGroupAsync(previousConnection, RoomNotifier.GroupName(code));
            }
            var result = await roomService.AttachDisplayAsync(code, Context.ConnectionAborted);
            logger.LogInformation("Display attached to room {RoomCode}", code);
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
            return (await roomService.GetAsync(roomCode, Context.ConnectionAborted)).ToSnapshot();
        }
        catch (RoomException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public async Task SubmitPlayerSelection(string roomCode, Guid playerId, string reconnectToken, Guid selectedPlayerId)
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
                Context.ConnectionAborted);

            await NotifyAsync(result);
        }
        catch (RoomException exception)
        {
            throw new HubException(exception.Message);
        }
    }

    public async Task SubmitTextAnswer(string roomCode, Guid playerId, string reconnectToken, string text)
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

    public async Task SubmitTextAnswerVote(string roomCode, Guid playerId, string reconnectToken, Guid selectedAnswerId)
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

    public async Task SubmitPhotoAnswerVote(string roomCode, Guid playerId, string reconnectToken, Guid questionInstanceId, Guid photoAnswerId)
    {
        try
        {
            if (!connectionRegistry.IsActivePlayer(Context.ConnectionId, roomCode.Trim(), playerId))
                throw new PhotoAnswerException("photo_answer_vote_player_not_eligible", "Not an active player.");
            var result = await roomService.SubmitPhotoAnswerVoteAsync(roomCode, playerId, reconnectToken, questionInstanceId, photoAnswerId, Context.ConnectionAborted);
            await NotifyAsync(result);
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(roomCode, playerId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("PlayerPrivateGameStateUpdated", privateState, Context.ConnectionAborted);
        }
        catch (RoomException exception)
        {
            throw new HubException(exception is PhotoAnswerException photo ? photo.Code : exception.Message);
        }
    }

    public async Task SubmitDrawingAnswerVote(string roomCode, Guid playerId, string reconnectToken, Guid questionInstanceId, Guid drawingAnswerId)
    {
        try { if (!connectionRegistry.IsActivePlayer(Context.ConnectionId, roomCode.Trim(), playerId)) throw new DrawingAnswerException("drawing_answer_vote_player_not_eligible", "Not an active player."); var result = await roomService.SubmitDrawingAnswerVoteAsync(roomCode, playerId, reconnectToken, questionInstanceId, drawingAnswerId, Context.ConnectionAborted); await NotifyAsync(result); var state = await roomService.GetPlayerPrivateGameStateAsync(roomCode, playerId, Context.ConnectionAborted); await Clients.Caller.SendAsync("PlayerPrivateGameStateUpdated", state, Context.ConnectionAborted); }
        catch (RoomException exception) { throw new HubException(exception is DrawingAnswerException drawing ? drawing.Code : exception.Message); }
    }

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
                    logger.LogInformation("Display disconnected from room {RoomCode}", assignment.RoomCode);
                }
                await NotifyAsync(result);
            }
            catch (RoomException roomException)
            {
                logger.LogWarning(roomException, "Could not update room after SignalR disconnection");
            }
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
    }
}

public sealed record HubPingResponse(string Status, DateTimeOffset UtcTime);
