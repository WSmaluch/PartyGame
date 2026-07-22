using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace PartyGame.Tests.Api;

public sealed class PartyGameApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _deleteOnDispose;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureServices;

    public PartyGameApiFactory()
        : this(Path.Combine(Path.GetTempPath(), "PartyGame.Tests", Guid.NewGuid().ToString("N")))
    {
    }

    internal PartyGameApiFactory(
        string temporaryDirectory,
        bool deleteOnDispose = true,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        TemporaryDirectory = temporaryDirectory;
        _deleteOnDispose = deleteOnDispose;
        _settings = settings ?? new Dictionary<string, string?>();
        _configureServices = configureServices;
    }

    public string TemporaryDirectory { get; }
    public string DatabasePath => Path.Combine(TemporaryDirectory, "test.db");
    public string MediaRootPath => Path.Combine(TemporaryDirectory, "media");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(TemporaryDirectory);
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:PartyGame", $"Data Source={DatabasePath}");
        builder.UseSetting("MediaStorage:RootPath", MediaRootPath);
        foreach (var setting in _settings)
        {
            builder.UseSetting(setting.Key, setting.Value);
        }
        if (_configureServices is not null)
        {
            builder.ConfigureServices(_configureServices);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && _deleteOnDispose && Directory.Exists(TemporaryDirectory))
        {
            Directory.Delete(TemporaryDirectory, recursive: true);
        }
    }
}
