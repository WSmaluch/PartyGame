using System.Net;
using System.Net.Http.Headers;
using PartyGame.Api.Hubs;
using PartyGame.Api.Security;

namespace PartyGame.Tests.Api;

public sealed class SecurityHardeningTests
{
    private const string OperatorToken = "security-test-operator-token-0123456789";

    [Fact]
    public void OperatorToken_RejectsPlaceholderAndUsesExactMatch()
    {
        var configured = new OperatorTokenOptions { Token = OperatorToken };
        Assert.True(configured.IsConfigured);
        Assert.True(configured.Matches(OperatorToken));
        Assert.False(configured.Matches(OperatorToken + "x"));
        Assert.False(configured.Matches(null));
        Assert.False(new OperatorTokenOptions { Token = "REPLACE_WITH_A_RANDOM_OPERATOR_TOKEN_AT_LEAST_32_CHARACTERS" }.IsConfigured);
    }

    [Fact]
    public async Task AdminEndpoints_RequireBearerWhenOperatorTokenIsConfigured()
    {
        using var factory = new PartyGameApiFactory(
            Path.Combine(Path.GetTempPath(), "PartyGame.Tests", Guid.NewGuid().ToString("N")),
            settings: new Dictionary<string, string?> { ["Security:Operator:Token"] = OperatorToken });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/content-packages")).StatusCode);
        using var malformed = new HttpRequestMessage(HttpMethod.Get, "/api/admin/content-packages");
        malformed.Headers.Authorization = new AuthenticationHeaderValue("Basic", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(malformed)).StatusCode);
        using var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/admin/content-packages");
        wrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(wrong)).StatusCode);
        using var allowed = new HttpRequestMessage(HttpMethod.Get, "/api/admin/content-packages");
        allowed.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OperatorToken);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(allowed)).StatusCode);
    }

    [Fact]
    public async Task Health_HasSecurityHeaders()
    {
        using var factory = new PartyGameApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        foreach (var header in new[] { "X-Content-Type-Options", "Referrer-Policy", "X-Frame-Options", "Permissions-Policy", "Content-Security-Policy" })
            Assert.True(response.Headers.Contains(header), $"Missing security header {header}");
        Assert.Contains("connect-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());
    }

    [Fact]
    public void SignalRConnectionRegistry_DoesNotPermitIdentityOrRoleSwitch()
    {
        var registry = new RoomConnectionRegistry();
        var playerA = Guid.NewGuid();
        var playerB = Guid.NewGuid();
        registry.AttachPlayer("connection", "ABCD", playerA);

        Assert.True(registry.CanAttachPlayer("connection", "ABCD", playerA));
        Assert.False(registry.CanAttachPlayer("connection", "ABCD", playerB));
        Assert.False(registry.CanAttachDisplay("connection", "ABCD"));
        Assert.True(registry.HasRoomAccess("connection", "abcd"));
        Assert.Equal(playerA, registry.RemoveIfActive("connection")?.PlayerId);
    }
}
