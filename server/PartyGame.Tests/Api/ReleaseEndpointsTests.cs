using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PartyGame.Api.Health;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;

namespace PartyGame.Tests.Api;

public sealed class ReleaseEndpointsTests(PartyGameApiFactory factory)
    : IClassFixture<PartyGameApiFactory>
{
    [Fact]
    public async Task GetSystemVersion_ReturnsSafeBuildMetadata()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/system/version");
        var body = await response.Content.ReadFromJsonAsync<SystemVersionResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.Version));
        Assert.False(string.IsNullOrWhiteSpace(body.InformationalVersion));
        Assert.Equal("Development", body.Environment);
        Assert.DoesNotContain("Path", string.Join('|', body.CommitHash, body.BuildTimestampUtc), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetReadiness_ReturnsReadyForFactoryRuntime()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadFromJsonAsync<RuntimeReadinessResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("ready", body.Status);
        Assert.Equal("ready", body.Database);
        Assert.Equal("ready", body.MediaStorage);
    }

    [Fact]
    public async Task Readiness_ReportsUnavailableMediaWithoutWritingAProbeFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "PartyGame.Tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(root, "test.db");
        var missingMediaPath = Path.Combine(root, "missing-media");
        Directory.CreateDirectory(root);
        try
        {
            var provider = CreateProvider($"Data Source={databasePath}");
            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().Database.EnsureCreatedAsync();
            }

            var result = await RuntimeReadiness.CheckAsync(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new MediaOptions { RootPath = missingMediaPath }),
                CancellationToken.None);

            Assert.Equal("not-ready", result.Status);
            Assert.Equal("ready", result.Database);
            Assert.Equal("unavailable", result.MediaStorage);
            Assert.False(Directory.Exists(missingMediaPath));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Readiness_ReportsUnavailableDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "PartyGame.Tests", Guid.NewGuid().ToString("N"));
        var mediaPath = Path.Combine(root, "media");
        Directory.CreateDirectory(mediaPath);
        try
        {
            var provider = CreateProvider("Data Source=/dev/null/partygame.db");
            var result = await RuntimeReadiness.CheckAsync(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new MediaOptions { RootPath = mediaPath }),
                CancellationToken.None);

            Assert.Equal("not-ready", result.Status);
            Assert.Equal("unavailable", result.Database);
            Assert.Equal("ready", result.MediaStorage);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ServiceProvider CreateProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<PartyGameDbContext>(options => options.UseSqlite(connectionString));
        return services.BuildServiceProvider();
    }

    private sealed record SystemVersionResponse(string Version, string InformationalVersion, string CommitHash, string BuildTimestampUtc, string Environment);
    private sealed record RuntimeReadinessResponse(string Status, string Database, string MediaStorage);
}
