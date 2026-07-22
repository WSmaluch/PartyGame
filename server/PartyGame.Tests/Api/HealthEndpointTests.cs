using System.Net;
using System.Net.Http.Json;

namespace PartyGame.Tests.Api;

public sealed class HealthEndpointTests(PartyGameApiFactory factory)
    : IClassFixture<PartyGameApiFactory>
{
    [Fact]
    public async Task GetHealth_ReturnsExpectedServiceStatus()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.Equal("PartyGame.Api", body.Service);
        Assert.False(string.IsNullOrWhiteSpace(body.Version));
        Assert.NotEqual(default, body.UtcTime);
    }

    private sealed record HealthResponse(
        string Status,
        string Service,
        string Version,
        DateTimeOffset UtcTime);
}
