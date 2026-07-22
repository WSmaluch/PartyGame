using PartyGame.Domain.Rooms;
using PartyGame.GameEngine;

namespace PartyGame.Tests.GameEngine;

public sealed class RoomStartEvaluatorTests
{
    [Fact]
    public void CanStart_RequiresEveryLobbyCondition()
    {
        Assert.False(RoomStartEvaluator.CanStart(WithDisplay(EligibleRoom(), false)));
        Assert.False(RoomStartEvaluator.CanStart(EligibleRoom(2)));
        Assert.False(RoomStartEvaluator.CanStart(EligibleRoom(change: player => player.HasProfilePhoto = false)));
        Assert.False(RoomStartEvaluator.CanStart(EligibleRoom(change: player => player.IsConnected = false)));
        Assert.False(RoomStartEvaluator.CanStart(EligibleRoom(change: player => player.IsReady = false)));
        Assert.True(RoomStartEvaluator.CanStart(EligibleRoom()));
    }

    [Fact]
    public void TryStart_StartsExactlyOnce()
    {
        var room = EligibleRoom();
        var time = DateTimeOffset.UtcNow;
        Assert.True(RoomStartEvaluator.TryStart(room, time));
        Assert.False(RoomStartEvaluator.TryStart(room, time.AddSeconds(1)));
        Assert.Equal(RoomPhase.Started, room.Phase);
        Assert.Equal(2, room.StateVersion);
        Assert.Equal(time, room.StartedAtUtc);
    }

    private static GameRoom EligibleRoom(int count = 3, Action<Player>? change = null)
    {
        var players = Enumerable.Range(0, count).Select(_ => new Player { IsConnected = true, HasProfilePhoto = true, IsReady = true }).ToList();
        if (change is not null && players.Count > 0) change(players[0]);
        return new GameRoom { DisplayConnected = true, Players = players };
    }

    private static GameRoom WithDisplay(GameRoom room, bool connected)
    {
        room.DisplayConnected = connected;
        return room;
    }
}
