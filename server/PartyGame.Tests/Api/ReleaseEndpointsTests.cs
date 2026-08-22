using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PartyGame.Api.Health;
using PartyGame.Api.Configuration;
using PartyGame.Api.Diagnostics;
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
    public async Task GetDatabaseSchema_ReturnsSafeMigrationCompatibility()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/system/schema");
        var body = await response.Content.ReadFromJsonAsync<DatabaseSchemaStatus>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("compatible", body.DatabaseCompatibility);
        Assert.False(body.MigrationRequired);
        Assert.DoesNotContain(Path.DirectorySeparatorChar.ToString(), body.DatabaseSchemaVersion);
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
                await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().Database.MigrateAsync();
            }

            var result = await RuntimeReadiness.CheckAsync(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new MediaOptions { RootPath = missingMediaPath }),
                Options.Create(new DeploymentOptions()),
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
                Options.Create(new DeploymentOptions()),
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

    [Fact]
    public async Task Readiness_ReportsAnActiveDataOperation()
    {
        var root = Path.Combine(Path.GetTempPath(), "PartyGame.Tests", Guid.NewGuid().ToString("N"));
        var media = Path.Combine(root, "media");
        Directory.CreateDirectory(media);
        Directory.CreateDirectory(Path.Combine(root, "operations", "data-operation.lock"));
        try
        {
            var provider = CreateProvider($"Data Source={Path.Combine(root, "test.db")}");
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().Database.MigrateAsync();

            var result = await RuntimeReadiness.CheckAsync(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new MediaOptions { RootPath = media }),
                Options.Create(new DeploymentOptions()),
                CancellationToken.None);

            Assert.Equal("not-ready", result.Status);
            Assert.Equal("active", result.DataOperation);
            Assert.Equal("compatible", result.Schema);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Deployment_ServesStaticApps_AndUsesSpaFallbackWithoutCapturingApiOrHub()
    {
        var root = Path.Combine(Path.GetTempPath(), "PartyGame.Tests", Guid.NewGuid().ToString("N"));
        var display = Path.Combine(root, "display");
        var admin = Path.Combine(root, "admin");
        var player = Path.Combine(root, "player");
        Directory.CreateDirectory(display);
        Directory.CreateDirectory(admin);
        Directory.CreateDirectory(player);
        await File.WriteAllTextAsync(Path.Combine(display, "index.html"), "<html>display</html>");
        await File.WriteAllTextAsync(Path.Combine(admin, "index.html"), "<html>admin</html>");
        await File.WriteAllTextAsync(Path.Combine(player, "index.html"), "<html>player</html>");
        await File.WriteAllTextAsync(Path.Combine(display, "config.json"), "{\"app\":\"display\"}");
        await File.WriteAllTextAsync(Path.Combine(admin, "config.json"), "{\"app\":\"admin\"}");
        await File.WriteAllTextAsync(Path.Combine(player, "config.json"), "{\"app\":\"player\"}");
        Directory.CreateDirectory(Path.Combine(display, "assets"));
        Directory.CreateDirectory(Path.Combine(admin, "assets"));
        Directory.CreateDirectory(Path.Combine(player, "assets"));
        await File.WriteAllTextAsync(Path.Combine(display, "assets", "app.js"), "display asset");
        await File.WriteAllTextAsync(Path.Combine(admin, "assets", "app.js"), "admin asset");
        await File.WriteAllTextAsync(Path.Combine(player, "assets", "app.js"), "player asset");
        try
        {
            using var deploymentFactory = new PartyGameApiFactory(
                root,
                settings: new Dictionary<string, string?>
                {
                    ["Deployment:Enabled"] = "true",
                    ["Deployment:DisplayRoot"] = display,
                    ["Deployment:AdminRoot"] = admin,
                    ["Deployment:PlayerRoot"] = player,
                });
            using var client = deploymentFactory.CreateClient();

            Assert.Equal("<html>display</html>", await client.GetStringAsync("/display/"));
            Assert.Equal("<html>admin</html>", await client.GetStringAsync("/admin/"));
            Assert.Equal("<html>player</html>", await client.GetStringAsync("/play/"));
            Assert.Equal("<html>display</html>", await client.GetStringAsync("/display/room/ABCD"));
            Assert.Equal("<html>admin</html>", await client.GetStringAsync("/admin/content/packages"));
            Assert.Equal("<html>player</html>", await client.GetStringAsync("/play/room/AB12"));
            var displayConfig = await client.GetAsync("/display/config.json");
            var adminConfig = await client.GetAsync("/admin/config.json");
            var playerConfig = await client.GetAsync("/play/config.json");
            Assert.Equal(HttpStatusCode.OK, displayConfig.StatusCode);
            Assert.Equal("application/json", displayConfig.Content.Headers.ContentType?.MediaType);
            Assert.Equal("{\"app\":\"display\"}", await displayConfig.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, adminConfig.StatusCode);
            Assert.Equal("application/json", adminConfig.Content.Headers.ContentType?.MediaType);
            Assert.Equal("{\"app\":\"admin\"}", await adminConfig.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, playerConfig.StatusCode);
            Assert.Equal("application/json", playerConfig.Content.Headers.ContentType?.MediaType);
            Assert.Equal("{\"app\":\"player\"}", await playerConfig.Content.ReadAsStringAsync());
            Assert.Equal("display asset", await client.GetStringAsync("/display/assets/app.js"));
            Assert.Equal("admin asset", await client.GetStringAsync("/admin/assets/app.js"));
            var playerAsset = await client.GetAsync("/play/assets/app.js");
            Assert.Equal("player asset", await playerAsset.Content.ReadAsStringAsync());
            Assert.Contains("javascript", playerAsset.Content.Headers.ContentType?.MediaType, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/display/missing.js")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/missing.json")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/play/missing.js")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/not-a-spa-route")).StatusCode);
            Assert.NotEqual("text/html", (await client.PostAsync("/hubs/game/negotiate?negotiateVersion=1", null)).Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Deployment_PathTraversal_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            DeploymentConfiguration.ResolveStaticRoot("../display", "/published/api", "Deployment:DisplayRoot"));
        Assert.False(DeploymentConfiguration.IsValidPathBase("/display/../api"));
    }

    [Fact]
    public async Task Readiness_ReportsMissingDeploymentStaticRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "PartyGame.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var provider = CreateProvider($"Data Source={Path.Combine(root, "test.db")}");
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<PartyGameDbContext>().Database.MigrateAsync();
            var media = Path.Combine(root, "media");
            Directory.CreateDirectory(media);

            var result = await RuntimeReadiness.CheckAsync(
                provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new MediaOptions { RootPath = media }),
                Options.Create(new DeploymentOptions { Enabled = true, DisplayRoot = Path.Combine(root, "missing"), AdminRoot = root, PlayerRoot = root }),
                CancellationToken.None);

            Assert.Equal("not-ready", result.Status);
            Assert.Equal("unavailable", result.Display);
            Assert.Equal("unavailable", result.Admin);
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
        services.AddScoped<DatabaseSchemaService>();
        return services.BuildServiceProvider();
    }

    private sealed record SystemVersionResponse(string Version, string InformationalVersion, string CommitHash, string BuildTimestampUtc, string Environment);
    private sealed record RuntimeReadinessResponse(string Status, string Database, string MediaStorage, string Display, string Admin, string Player);
}
