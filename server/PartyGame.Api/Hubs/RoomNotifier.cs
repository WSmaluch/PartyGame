using Microsoft.AspNetCore.SignalR;
using PartyGame.Api.Contracts;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Api.Hubs;

public sealed class RoomNotifier(IHubContext<GameHub> hubContext)
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
    }
}
