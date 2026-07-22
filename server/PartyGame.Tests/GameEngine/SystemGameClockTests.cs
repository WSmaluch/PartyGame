using PartyGame.GameEngine;

namespace PartyGame.Tests.GameEngine;

public sealed class SystemGameClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentUtcTime()
    {
        var before = DateTimeOffset.UtcNow;
        var value = new SystemGameClock().UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(value, before, after);
        Assert.Equal(TimeSpan.Zero, value.Offset);
    }
}
