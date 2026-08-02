using System.Net;
using System.Net.Http.Headers;
using System.IO.Compression;
using System.Text.Json;

namespace PartyGame.Tests.Api;

public sealed class DiagnosticsEndpointsTests
{
    private const string OperatorToken = "diagnostics-test-operator-token-0123456789";

    [Fact]
    public async Task Health_GeneratesAndReturnsCorrelationId()
    {
        using var factory = new PartyGameApiFactory();
        using var response = await factory.CreateClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID", out var values));
        Assert.Matches("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", Assert.Single(values));
    }

    [Fact]
    public async Task Health_PreservesValidCorrelationId_AndReplacesUnsafeValue()
    {
        using var factory = new PartyGameApiFactory();
        using var client = factory.CreateClient();
        using var valid = new HttpRequestMessage(HttpMethod.Get, "/health");
        valid.Headers.Add("X-Correlation-ID", "operator-check_42");
        using var validResponse = await client.SendAsync(valid);
        Assert.Equal("operator-check_42", validResponse.Headers.GetValues("X-Correlation-ID").Single());

        using var unsafeRequest = new HttpRequestMessage(HttpMethod.Get, "/health");
        unsafeRequest.Headers.Add("X-Correlation-ID", "bad value with spaces");
        using var unsafeResponse = await client.SendAsync(unsafeRequest);
        Assert.NotEqual("bad value with spaces", unsafeResponse.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task OperatorDiagnostics_RequiresBearerAndReturnsNoSecretOrPath()
    {
        using var factory = new PartyGameApiFactory(Path.Combine(Path.GetTempPath(), "PartyGame.Tests", Guid.NewGuid().ToString("N")), settings: new Dictionary<string, string?> { ["Security:Operator:Token"] = OperatorToken });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/diagnostics/summary")).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/diagnostics/summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OperatorToken);
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(OperatorToken, body, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.TemporaryDirectory, body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("1", document.RootElement.GetProperty("version").GetProperty("apiContractVersion").GetString());
    }

    [Fact]
    public async Task OperatorSupportBundle_IsServerNamedAndExcludesDatabaseAndMedia()
    {
        var root = Path.Combine(Path.GetTempPath(), "PartyGame.Tests", Guid.NewGuid().ToString("N"));
        using var factory = new PartyGameApiFactory(root, settings: new Dictionary<string, string?>
        {
            ["Security:Operator:Token"] = OperatorToken,
            ["Diagnostics:SupportBundleDirectory"] = Path.Combine(root, "support-bundles")
        });
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/diagnostics/support-bundles?mode=minimal");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OperatorToken);
        using var created = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var status = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = status.RootElement.GetProperty("id").GetGuid();
        using var download = new HttpRequestMessage(HttpMethod.Get, $"/api/admin/diagnostics/support-bundles/{id}/download");
        download.Headers.Authorization = new AuthenticationHeaderValue("Bearer", OperatorToken);
        using var archiveResponse = await client.SendAsync(download);
        Assert.Equal(HttpStatusCode.OK, archiveResponse.StatusCode);
        await using var stream = await archiveResponse.Content.ReadAsStreamAsync();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, entry => entry.FullName == "support-manifest.json");
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith(".db", StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
    }
}
