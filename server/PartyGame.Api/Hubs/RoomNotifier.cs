using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using PartyGame.Api.Contracts;
using PartyGame.Domain.Game;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Api.Hubs;

public sealed class RoomNotifier(
    IHubContext<GameHub> hubContext,
    IRoomConnectionRegistry connectionRegistry,
    IServiceScopeFactory scopeFactory)
{
    public static string GroupName(string roomCode) => $"room:{roomCode.ToUpperInvariant()}";

    public async Task NotifyAsync(RoomMutationResult result, CancellationToken cancellationToken = default)
    {
        if (!result.PublicStateChanged)
        {
            return;
        }
        var snapshot = result.Room.ToSnapshot();
        if (result.StartedNow)
        {
            await hubContext.Clients.Group(GroupName(result.Room.Code)).SendAsync("RoomStarted", snapshot, cancellationToken);
        }
        await hubContext.Clients.Group(GroupName(result.Room.Code)).SendAsync("RoomSnapshotUpdated", snapshot, cancellationToken);
        await NotifyFinalRoundPrivateStatesAsync(result.Room, cancellationToken);
    }

    /// <summary>
    /// A public stage transition contains no per-player task data.  Deliver the
    /// current private contract alongside it to already-attached players, so a
    /// Final Round entry does not rely on reconnect/resume timing.
    /// </summary>
    private async Task NotifyFinalRoundPrivateStatesAsync(PartyGame.Domain.Rooms.GameRoom room, CancellationToken cancellationToken)
    {
        if (room.Session?.Stage is not (GameStage.CollectingFinalSelfies or GameStage.CollectingFinalEdits or GameStage.CollectingFinalVotes))
            return;

        await using var scope = scopeFactory.CreateAsyncScope();
        var roomService = scope.ServiceProvider.GetRequiredService<IRoomService>();
        foreach (var player in room.Players)
        {
            var connectionId = connectionRegistry.GetActivePlayerConnection(player.Id);
            if (connectionId is null)
                continue;
            var privateState = await roomService.GetPlayerPrivateGameStateAsync(room.Code, player.Id, cancellationToken);
            await hubContext.Clients.Client(connectionId).SendAsync("PlayerPrivateGameStateUpdated", privateState, cancellationToken);
        }
    }
}
