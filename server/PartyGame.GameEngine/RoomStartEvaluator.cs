using PartyGame.Domain.Rooms;

namespace PartyGame.GameEngine;

public static class RoomStartEvaluator
{
    public static bool CanStart(GameRoom room)
    {
        var activePlayers = room.Players.Where(player => player.IsActive).ToList();
        return room.Phase == RoomPhase.Lobby &&
            room.DisplayConnected &&
            activePlayers.Count is >= GameRoom.MinimumPlayers and <= GameRoom.MaximumPlayers &&
            activePlayers.All(player => player.IsConnected && player.HasProfilePhoto && player.IsReady);
    }

    public static bool TryStart(GameRoom room, DateTimeOffset utcNow)
    {
        if (!CanStart(room))
        {
            return false;
        }

        room.Phase = RoomPhase.Started;
        room.StartedAtUtc = utcNow;
        room.PublicStateChanged(utcNow);
        return true;
    }
}
