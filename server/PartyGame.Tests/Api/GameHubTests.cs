using PartyGame.Api.Hubs;
using PartyGame.GameEngine;

namespace PartyGame.Tests.Api;

public sealed class GameHubTests
{
    [Fact]
    public void Ping_ReturnsPongAndServerUtcTime()
    {
        var expectedTime = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var hub = new GameHub(new FixedClock(expectedTime));

        var response = hub.Ping();

        Assert.Equal("pong", response.Status);
        Assert.Equal(expectedTime, response.UtcTime);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
