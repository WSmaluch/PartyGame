using PartyGame.Domain.Rooms;
using PartyGame.Infrastructure.Rooms;

namespace PartyGame.Tests.Infrastructure;

public sealed class PlayerSessionServiceTests
{
    [Fact]
    public void Tokens_AreRandomAndHashesAreDeterministic()
    {
        var service = new PlayerSessionService();
        var first = service.GenerateToken();
        var second = service.GenerateToken();
        Assert.NotEqual(first, second);
        Assert.Equal(service.HashToken(first), service.HashToken(first));
        Assert.True(service.VerifyToken(first, service.HashToken(first)));
        Assert.False(service.VerifyToken(second, service.HashToken(first)));

        var session = new PlayerSession { ReconnectTokenHash = service.HashToken(first) };
        Assert.NotEqual(first, session.ReconnectTokenHash);
        Assert.DoesNotContain(typeof(PlayerSession).GetProperties(), property => property.Name == "ReconnectToken");
    }
}
