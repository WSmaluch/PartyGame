using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.StaticFiles;
using PartyGame.Api.Configuration;
using PartyGame.Api.Diagnostics;
using PartyGame.Api.Endpoints;
using PartyGame.Api.Health;
using PartyGame.Api.Hubs;
using PartyGame.Api.Security;
using PartyGame.Domain.Content;
using PartyGame.Domain.Rooms;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInMemoryCollection(ReleaseRuntimeConfiguration.EnvironmentOverrides());

var releaseRuntime = builder.Configuration.GetSection(ReleaseRuntimeOptions.SectionName).Get<ReleaseRuntimeOptions>() ?? new ReleaseRuntimeOptions();
var deployment = builder.Configuration.GetSection(DeploymentOptions.SectionName).Get<DeploymentOptions>() ?? new DeploymentOptions();
var configuredTransportSecurity = builder.Configuration.GetSection(TransportSecurityOptions.SectionName).Get<TransportSecurityOptions>() ?? new TransportSecurityOptions();
var diagnosticsOptions = builder.Configuration.GetSection(DiagnosticsOptions.SectionName).Get<DiagnosticsOptions>() ?? new DiagnosticsOptions();
if (builder.Environment.IsProduction() && !configuredTransportSecurity.AllowInsecureLanHttp &&
    (releaseRuntime.PublicBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
     releaseRuntime.ListeningUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
{
    throw new InvalidOperationException("Production HTTP requires PARTYGAME_ALLOW_INSECURE_LAN_HTTP=true; use HTTPS otherwise.");
}
if (builder.Environment.IsProduction())
{
    var databasePath = ReleaseRuntimeConfiguration.ResolveRuntimePath(
        releaseRuntime.DatabasePath,
        builder.Environment.ContentRootPath,
        "ReleaseRuntime:DatabasePath",
        mustBeOutsideContentRoot: true);
    var mediaRoot = ReleaseRuntimeConfiguration.ResolveRuntimePath(
        releaseRuntime.MediaRoot,
        builder.Environment.ContentRootPath,
        "ReleaseRuntime:MediaRoot",
        mustBeOutsideContentRoot: true);
    // Deployment scripts set these paths explicitly. The fallback keeps an
    // isolated release-smoke runtime safe as well: diagnostic output must never
    // fall back into the immutable publish directory.
    if (string.Equals(diagnosticsOptions.LogDirectory, "data/logs", StringComparison.OrdinalIgnoreCase))
        diagnosticsOptions.LogDirectory = Path.Combine(Path.GetDirectoryName(databasePath)!, "logs");
    if (string.Equals(diagnosticsOptions.SupportBundleDirectory, "data/support-bundles", StringComparison.OrdinalIgnoreCase))
        diagnosticsOptions.SupportBundleDirectory = Path.Combine(Path.GetDirectoryName(databasePath)!, "support-bundles");
    builder.Configuration["ConnectionStrings:PartyGame"] = $"Data Source={databasePath}";
    builder.Configuration["MediaStorage:RootPath"] = mediaRoot;
    diagnosticsOptions.LogDirectory = ReleaseRuntimeConfiguration.ResolveRuntimePath(
        diagnosticsOptions.LogDirectory, builder.Environment.ContentRootPath, "Diagnostics:LogDirectory", mustBeOutsideContentRoot: true);
    diagnosticsOptions.SupportBundleDirectory = ReleaseRuntimeConfiguration.ResolveRuntimePath(
        diagnosticsOptions.SupportBundleDirectory, builder.Environment.ContentRootPath, "Diagnostics:SupportBundleDirectory", mustBeOutsideContentRoot: true);
    builder.Configuration["Diagnostics:LogDirectory"] = diagnosticsOptions.LogDirectory;
    builder.Configuration["Diagnostics:SupportBundleDirectory"] = diagnosticsOptions.SupportBundleDirectory;
}

var resolvedLogDirectory = Path.GetFullPath(diagnosticsOptions.LogDirectory);
Directory.CreateDirectory(resolvedLogDirectory);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration.ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "PartyGame.Api");
    var file = Path.Combine(resolvedLogDirectory, "partygame-.log");
    if (diagnosticsOptions.IsJson)
    {
        loggerConfiguration.WriteTo.File(new CompactJsonFormatter(), file, rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: diagnosticsOptions.LogFileSizeLimitBytes, rollOnFileSizeLimit: true,
            retainedFileCountLimit: diagnosticsOptions.LogRetainedFileCount, shared: true, flushToDiskInterval: TimeSpan.FromSeconds(1));
    }
    else
    {
        loggerConfiguration.WriteTo.File(path: file, outputTemplate: "[{Timestamp:O} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
            rollingInterval: RollingInterval.Day, fileSizeLimitBytes: diagnosticsOptions.LogFileSizeLimitBytes,
            rollOnFileSizeLimit: true, retainedFileCountLimit: diagnosticsOptions.LogRetainedFileCount,
            shared: true, flushToDiskInterval: TimeSpan.FromSeconds(1));
    }
});

builder.Services.AddProblemDetails();
builder.Services.Configure<FormOptions>(options =>
{
    // The largest accepted media payload is 10 MiB. Keep multipart framing bounded
    // so oversized files are rejected before model binding reaches storage.
    options.MultipartBodyLengthLimit = 11 * 1024 * 1024;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR().AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<IGameClock, SystemGameClock>();
builder.Services.AddSingleton<IRandomProvider, SystemRandomProvider>();
builder.Services.AddSingleton<IRoomCodeGenerator, RoomCodeGenerator>();
builder.Services.AddSingleton<IPlayerSessionService, PlayerSessionService>();
builder.Services.AddSingleton<RoomLockProvider>();
builder.Services.AddSingleton<IRoomConnectionRegistry, RoomConnectionRegistry>();
builder.Services.AddSingleton<BuildVersionInfo>();
builder.Services.AddSingleton<IRuntimeDiagnosticsService, RuntimeDiagnosticsService>();
builder.Services.AddSingleton<ISupportBundleService, SupportBundleService>();
builder.Services.AddScoped<GamePlanner>();
builder.Services.AddScoped<IContentValidationService, PartyGame.Infrastructure.Content.ContentValidationService>();
builder.Services.AddSingleton<PartyGame.Infrastructure.Content.ContentPackageLockProvider>();
builder.Services.AddScoped<ScoreCalculator>();
builder.Services.AddScoped<GameStateMachine>();
builder.Services.AddHostedService<PartyGame.Api.BackgroundServices.GameEngineWorker>();
builder.Services.AddScoped<IRoomService, RoomService>();
builder.Services.AddOptions<PartyGame.Infrastructure.Rooms.GameFlowOptions>()
    .Bind(builder.Configuration.GetSection(PartyGame.Infrastructure.Rooms.GameFlowOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<ReleaseRuntimeOptions>()
    .Bind(builder.Configuration.GetSection(ReleaseRuntimeOptions.SectionName))
    .Validate(options => !builder.Environment.IsProduction() || !string.IsNullOrWhiteSpace(options.DatabasePath), "ReleaseRuntime:DatabasePath is required in Production.")
    .Validate(options => !builder.Environment.IsProduction() || !string.IsNullOrWhiteSpace(options.MediaRoot), "ReleaseRuntime:MediaRoot is required in Production.")
    .Validate(options => !builder.Environment.IsProduction() || ReleaseRuntimeConfiguration.IsValidHttpUrl(options.PublicBaseUrl), "ReleaseRuntime:PublicBaseUrl must be an absolute http or https URL in Production.")
    .Validate(options => !builder.Environment.IsProduction() || ReleaseRuntimeConfiguration.IsValidHttpUrl(options.ListeningUrl), "ReleaseRuntime:ListeningUrl (PARTYGAME_URLS) must be an absolute http or https URL in Production.")
    .Validate(options => !builder.Environment.IsProduction() || options.AllowedOrigins.Length > 0, "ReleaseRuntime:AllowedOrigins (PARTYGAME_ALLOWED_ORIGINS) must contain at least one explicit origin in Production.")
    .Validate(options => !builder.Environment.IsProduction() || options.AllowedOrigins.All(ReleaseRuntimeConfiguration.IsValidOrigin), "ReleaseRuntime:AllowedOrigins must contain only explicit http or https origins; wildcard origins are not allowed in Production.")
    .ValidateOnStart();
builder.Services.AddOptions<DeploymentOptions>()
    .Bind(builder.Configuration.GetSection(DeploymentOptions.SectionName))
    .Validate(options => !options.Enabled || (!string.IsNullOrWhiteSpace(options.DisplayRoot) && !string.IsNullOrWhiteSpace(options.AdminRoot) && !string.IsNullOrWhiteSpace(options.PlayerRoot)), "Deployment:DisplayRoot, Deployment:AdminRoot and Deployment:PlayerRoot are required when Deployment is enabled.")
    .Validate(options => !options.Enabled || (DeploymentConfiguration.IsValidPathBase(options.DisplayPathBase) && DeploymentConfiguration.IsValidPathBase(options.AdminPathBase) && DeploymentConfiguration.IsValidPathBase(options.PlayerPathBase) && !string.Equals(options.DisplayPathBase, options.AdminPathBase, StringComparison.OrdinalIgnoreCase) && !string.Equals(options.DisplayPathBase, options.PlayerPathBase, StringComparison.OrdinalIgnoreCase) && !string.Equals(options.AdminPathBase, options.PlayerPathBase, StringComparison.OrdinalIgnoreCase)), "Deployment path bases must be distinct absolute single-root paths without traversal.")
    .ValidateOnStart();
builder.Services.AddOptions<MediaOptions>()
    .Bind(builder.Configuration.GetSection(MediaOptions.SectionName))
    .Validate(options => options.Provider == "LocalFileSystem", "Only the LocalFileSystem media provider is available in this release.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "MediaStorage:RootPath is required.")
    .Validate(options => options.UntrackedFileCleanupGracePeriodMinutes > 0, "MediaStorage:UntrackedFileCleanupGracePeriodMinutes must be greater than zero.")
    .Validate(options => options.DiagnosticsCacheSeconds >= 0, "MediaStorage:DiagnosticsCacheSeconds must not be negative.")
    .Validate(options => options.CriticalFreePercent > 0, "MediaStorage:CriticalFreePercent must be greater than zero.")
    .Validate(options => options.WarningFreePercent > options.CriticalFreePercent && options.WarningFreePercent <= 100, "MediaStorage:WarningFreePercent must be greater than CriticalFreePercent and at most 100.")
    .ValidateOnStart();
builder.Services.Configure<DrawingMediaOptions>(builder.Configuration.GetSection(DrawingMediaOptions.SectionName));
builder.Services.AddSingleton<IMediaStorage, LocalMediaStorage>();
builder.Services.AddSingleton<ILocalMediaFileCatalog, LocalMediaFileCatalog>();
builder.Services.AddSingleton<IMediaStorageProbe, LocalMediaStorageProbe>();
builder.Services.AddSingleton<IStorageVolumeInfoProvider, LocalStorageVolumeInfoProvider>();
builder.Services.AddSingleton<IMediaStorageDiagnosticsService, LocalMediaStorageDiagnosticsService>();
builder.Services.AddHealthChecks().AddCheck<MediaStorageHealthCheck>("media-storage", tags: ["storage"]);
builder.Services.AddScoped<IProfilePhotoCleanupService, ProfilePhotoCleanupService>();
builder.Services.AddScoped<IOrphanedGameMediaCleanupService, OrphanedGameMediaCleanupService>();
builder.Services.AddScoped<IUntrackedMediaFileCleanupService, UntrackedMediaFileCleanupService>();
builder.Services.AddSingleton<PartyGame.Api.Contracts.IPhotoMediaUrlProvider, PartyGame.Api.Contracts.PhotoMediaUrlProvider>();
builder.Services.AddSingleton<RoomNotifier>();
builder.Services.AddDbContext<PartyGameDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("PartyGame")));
builder.Services.AddScoped<DatabaseSchemaService>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<OperatorTokenOptions>>().Value);
builder.Services.AddOptions<OperatorTokenOptions>().Bind(builder.Configuration.GetSection(OperatorTokenOptions.SectionName))
    .Validate(options => !builder.Environment.IsProduction() || options.IsConfigured, "PARTYGAME_OPERATOR_TOKEN must be a non-placeholder value of at least 32 characters in Production.")
    .ValidateOnStart();
builder.Services.AddOptions<TransportSecurityOptions>()
    .Bind(builder.Configuration.GetSection(TransportSecurityOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<DiagnosticsOptions>()
    .Bind(builder.Configuration.GetSection(DiagnosticsOptions.SectionName))
    .Validate(options => options.LogFileSizeLimitMb is >= 1 and <= 1024, "Diagnostics:LogFileSizeLimitMb must be in 1..1024.")
    .Validate(options => options.LogRetainedFileCount is >= 1 and <= 365, "Diagnostics:LogRetainedFileCount must be in 1..365.")
    .Validate(options => string.Equals(options.LogFormat, "json", StringComparison.OrdinalIgnoreCase) || string.Equals(options.LogFormat, "text", StringComparison.OrdinalIgnoreCase), "Diagnostics:LogFormat must be json or text.")
    .ValidateOnStart();

var allowedOrigins = builder.Environment.IsProduction()
    ? releaseRuntime.AllowedOrigins
    : builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalClients", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.SetIsOriginAllowed(_ => true);
        }
        else
        {
            policy.WithOrigins(allowedOrigins);
        }

        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});
builder.Services.AddRateLimiter(options =>
{
    // Development/test hosts retain active limiters but use a clearly documented
    // high ceiling so parallel integration and Mixed Client scenarios are not
    // accidentally serialized by a shared loopback address.
    var roomPermitLimit = builder.Environment.IsDevelopment() ? 10_000 : 120;
    var uploadPermitLimit = builder.Environment.IsDevelopment() ? 1_000 : 12;
    var operatorPermitLimit = builder.Environment.IsDevelopment() ? 10_000 : 20;
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("room-operations", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{context.Connection.RemoteIpAddress}|{RoomRatePartition(context.Request.Path)}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = roomPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("uploads", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{context.Connection.RemoteIpAddress}|{RoomRatePartition(context.Request.Path)}",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = uploadPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
    options.AddPolicy("operator", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = operatorPermitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});

var app = builder.Build();

var dataOperation = args.SingleOrDefault(argument => argument is "check" or "migrate");
if (dataOperation is not null)
{
    await using var operationScope = app.Services.CreateAsyncScope();
    var schema = operationScope.ServiceProvider.GetRequiredService<DatabaseSchemaService>();
    var result = dataOperation == "migrate"
        ? await schema.MigrateAsync()
        : await schema.GetStatusAsync();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    return;
}

app.Use(CorrelationId.UseAsync);
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var correlationId = context.Items[CorrelationId.HeaderName]?.ToString() ?? CorrelationId.Create();
        IResult result = exception switch
        {
            DomainValidationException validation => Results.ValidationProblem(validation.Errors, statusCode: StatusCodes.Status400BadRequest, extensions: ErrorExtensions("INVALID_REQUEST", correlationId)),
            RoomNotFoundException => SafeProblem(StatusCodes.Status404NotFound, "ROOM_NOT_FOUND", exception.Message, correlationId),
            PlayerNotFoundException => SafeProblem(StatusCodes.Status404NotFound, "PLAYER_NOT_FOUND", exception.Message, correlationId),
            InvalidPlayerTokenException => SafeProblem(StatusCodes.Status401Unauthorized, "AUTH_INVALID", "Authentication is invalid.", correlationId),
            PhotoAnswerException photo => SafeProblem(PhotoStatus(photo.Code), photo.Code, photo.Message, correlationId),
            DrawingAnswerException drawing => SafeProblem(DrawingStatus(drawing.Code), drawing.Code, drawing.Message, correlationId),
            RoomConflictException or RoomCodeGenerationException => SafeProblem(StatusCodes.Status409Conflict, "SUBMISSION_CONFLICT", exception.Message, correlationId),
            DbUpdateConcurrencyException => SafeProblem(StatusCodes.Status409Conflict, "SUBMISSION_CONFLICT", "The requested data changed concurrently; refresh and retry.", correlationId),
            DbUpdateException { InnerException: SqliteException { SqliteErrorCode: 5 or 6 } } => SafeProblem(StatusCodes.Status409Conflict, "SUBMISSION_CONFLICT", "The requested data changed concurrently; refresh and retry.", correlationId),
            _ => SafeProblem(StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "An unexpected error occurred.", correlationId)
        };
        if (exception is RoomException or DomainValidationException)
        {
            app.Logger.LogInformation("Request rejected for {Path}: {Reason}", context.Request.Path, exception.Message);
        }
        else
        {
            app.Logger.LogError(exception, "Unhandled exception while processing {Path}", context.Request.Path);
        }
        await result.ExecuteAsync(context);
    });
});

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("Referrer-Policy", "no-referrer");
        headers.TryAdd("X-Frame-Options", "DENY");
        headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()");
        headers.TryAdd("Content-Security-Policy", "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; script-src 'self'; style-src 'self'; img-src 'self' blob: data:; connect-src 'self'; object-src 'none'");
        return Task.CompletedTask;
    });
    await next();
});

if (app.Environment.IsDevelopment() || releaseRuntime.ApplyMigrations)
{
    app.UseSwagger();
    app.UseSwaggerUI();

    await using var scope = app.Services.CreateAsyncScope();
    var schema = scope.ServiceProvider.GetRequiredService<DatabaseSchemaService>();
    await schema.MigrateAsync();
}
else if (app.Environment.IsProduction())
{
    await using var scope = app.Services.CreateAsyncScope();
    var schema = scope.ServiceProvider.GetRequiredService<DatabaseSchemaService>();
    var status = await schema.GetStatusAsync();
    if (status.DatabaseCompatibility != "compatible" || status.MigrationRequired)
        throw new InvalidOperationException("Database schema is not compatible with this release; run the explicit migrate operation before starting the API.");
}

await using (var scope = app.Services.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
    var clock = scope.ServiceProvider.GetRequiredService<IGameClock>();
    var profilePhotoCleanup = scope.ServiceProvider.GetRequiredService<IProfilePhotoCleanupService>();
    var orphanedGameMediaCleanup = scope.ServiceProvider.GetRequiredService<IOrphanedGameMediaCleanupService>();
    var untrackedMediaFileCleanup = scope.ServiceProvider.GetRequiredService<IUntrackedMediaFileCleanupService>();

    await PartyGame.Infrastructure.Persistence.Seed.ContentSeeder.SeedAsync(dbContext, clock);
    await BackfillProfilePhotos.RunAsync(scope.ServiceProvider);
    await profilePhotoCleanup.CleanupUnusedAsync();
    await orphanedGameMediaCleanup.CleanupUnusedAsync();
    try
    {
        var cleanupResult = await untrackedMediaFileCleanup.CleanupAsync();
        app.Logger.LogInformation(
            "Untracked media file startup cleanup completed: {Scanned} scanned, {Candidates} candidates, {Deleted} deleted, {SkippedReferenced} referenced, {SkippedTooYoung} too young, {Missing} missing, {Failed} failed",
            cleanupResult.Scanned,
            cleanupResult.Candidates,
            cleanupResult.Deleted,
            cleanupResult.SkippedReferenced,
            cleanupResult.SkippedTooYoung,
            cleanupResult.Missing,
            cleanupResult.Failed);
    }
    catch (Exception exception)
    {
        app.Logger.LogWarning(
            "Untracked media file startup cleanup failed; error type {ErrorType}",
            exception.GetType().Name);
    }

    var roomsWithLiveConnections = await dbContext.GameRooms
        .Include(room => room.Players)
        .Where(room => room.DisplayConnected || room.Players.Any(player => player.IsConnected))
        .ToListAsync();
    foreach (var room in roomsWithLiveConnections)
    {
        room.DisplayConnected = false;
        foreach (var player in room.Players)
        {
            player.IsConnected = false;
        }
        room.PublicStateChanged(DateTimeOffset.UtcNow);
    }
    if (roomsWithLiveConnections.Count > 0)
    {
        await dbContext.SaveChangesAsync();
        app.Logger.LogInformation("Reset stale lobby connections in {RoomCount} rooms after startup", roomsWithLiveConnections.Count);
    }
}

app.UseCors("LocalClients");
app.UseRateLimiter();

var transportSecurity = app.Services.GetRequiredService<IOptions<TransportSecurityOptions>>().Value;
if (transportSecurity.EnableHsts)
    app.UseHsts();
if (app.Environment.IsProduction() && transportSecurity.AllowInsecureLanHttp &&
    (releaseRuntime.PublicBaseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || releaseRuntime.ListeningUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
{
    app.Logger.LogWarning("Trusted LAN HTTP is enabled. HTTP is intended only for a trusted private LAN.");
}

if (deployment.Enabled)
{
    var displayRoot = DeploymentConfiguration.ResolveStaticRoot(deployment.DisplayRoot, app.Environment.ContentRootPath, "Deployment:DisplayRoot");
    var adminRoot = DeploymentConfiguration.ResolveStaticRoot(deployment.AdminRoot, app.Environment.ContentRootPath, "Deployment:AdminRoot");
    var playerRoot = DeploymentConfiguration.ResolveStaticRoot(deployment.PlayerRoot, app.Environment.ContentRootPath, "Deployment:PlayerRoot");
    if (!Directory.Exists(displayRoot) || !File.Exists(Path.Combine(displayRoot, "index.html")))
        app.Logger.LogError("Deployment Display root is unavailable or missing index.html: {DisplayRoot}", displayRoot);
    if (!Directory.Exists(adminRoot) || !File.Exists(Path.Combine(adminRoot, "index.html")))
        app.Logger.LogError("Deployment Admin root is unavailable or missing index.html: {AdminRoot}", adminRoot);
    if (!Directory.Exists(playerRoot) || !File.Exists(Path.Combine(playerRoot, "index.html")))
        app.Logger.LogError("Deployment Player root is unavailable or missing index.html: {PlayerRoot}", playerRoot);
    app.MapMethods($"{deployment.DisplayPathBase}/{{**path}}", [HttpMethods.Get, HttpMethods.Head],
        (string? path) => StaticFileOrSpaFallback(displayRoot, path));
    app.MapMethods($"{deployment.AdminPathBase}/{{**path}}", [HttpMethods.Get, HttpMethods.Head],
        (string? path) => StaticFileOrSpaFallback(adminRoot, path));
    app.MapMethods($"{deployment.PlayerPathBase}/{{**path}}", [HttpMethods.Get, HttpMethods.Head],
        (string? path) => StaticFileOrSpaFallback(playerRoot, path));
}

static IResult StaticFileOrSpaFallback(string staticRoot, string? relativePath)
{
    var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(staticRoot));
    var relative = relativePath?.TrimStart('/') ?? string.Empty;
    var candidate = Path.GetFullPath(Path.Combine(root, relative));
    if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !string.Equals(candidate, root, StringComparison.Ordinal))
        return Results.NotFound();

    if (File.Exists(candidate))
    {
        var contentTypes = new FileExtensionContentTypeProvider();
        return Results.File(candidate, contentTypes.TryGetContentType(candidate, out var contentType) ? contentType : "application/octet-stream");
    }

    return Path.HasExtension(relative)
        ? Results.NotFound()
        : Results.File(Path.Combine(root, "index.html"), "text/html");
}

app.MapGet("/health", (IGameClock clock) =>
    Results.Ok(new HealthResponse(
        "ok",
        "PartyGame.Api",
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
        clock.UtcNow)))
    .WithName("GetHealth")
    .Produces<HealthResponse>(StatusCodes.Status200OK);

app.MapGet("/health/ready", async (
    IServiceScopeFactory scopeFactory,
    IOptions<MediaOptions> mediaOptions,
    IOptions<DeploymentOptions> deploymentOptions,
    CancellationToken cancellationToken) =>
{
    var readiness = await RuntimeReadiness.CheckAsync(scopeFactory, mediaOptions, deploymentOptions, cancellationToken);
    return readiness.Status == "ready"
        ? Results.Ok(readiness)
        : Results.Json(readiness, statusCode: StatusCodes.Status503ServiceUnavailable);
})
    .WithName("GetRuntimeReadiness")
    .Produces<RuntimeReadinessResult>(StatusCodes.Status200OK)
    .Produces<RuntimeReadinessResult>(StatusCodes.Status503ServiceUnavailable);

app.MapGet("/api/system/version", async (BuildVersionInfo buildVersion, DatabaseSchemaService schema, CancellationToken cancellationToken) =>
{
    var status = await schema.GetStatusAsync(cancellationToken);
    var contract = buildVersion.ToContract(status.DatabaseSchemaVersion);
    return Results.Ok(new SystemVersionResponse(contract.ApplicationVersion, contract.InformationalVersion, contract.CommitHash, contract.BuildTimestampUtc, contract.Environment, contract.ApiContractVersion, contract.SignalRContractVersion, contract.DatabaseSchemaVersion, contract.BackupFormatVersion, contract.SupportBundleFormatVersion, contract.DisplayVersion, contract.AdminVersion, contract.IosClientVersion, contract.MinimumSupportedIosVersion));
})
    .WithName("GetSystemVersion")
    .Produces<SystemVersionResponse>(StatusCodes.Status200OK);

app.MapGet("/api/system/schema", async (DatabaseSchemaService schema, CancellationToken cancellationToken) =>
    Results.Ok(await schema.GetStatusAsync(cancellationToken)))
    .WithName("GetDatabaseSchema")
    .Produces<DatabaseSchemaStatus>(StatusCodes.Status200OK);

app.MapGet("/health/storage", async (
    IMediaStorageDiagnosticsService diagnostics,
    CancellationToken cancellationToken) =>
{
    var result = await diagnostics.GetAsync(cancellationToken);
    var statusCode = result.Status is MediaStorageDiagnosticStatus.Unhealthy or MediaStorageDiagnosticStatus.NotSupported
        ? StatusCodes.Status503ServiceUnavailable
        : StatusCodes.Status200OK;
    return Results.Json(result, statusCode: statusCode);
})
    .WithName("GetMediaStorageHealth")
    .Produces<MediaStorageDiagnosticsResult>(StatusCodes.Status200OK)
    .Produces<MediaStorageDiagnosticsResult>(StatusCodes.Status503ServiceUnavailable);

var diagnosticsAdmin = app.MapGroup("/api/admin/diagnostics")
    .AddEndpointFilter<OperatorTokenEndpointFilter>()
    .RequireRateLimiting("operator");
diagnosticsAdmin.MapGet("/summary", async (IRuntimeDiagnosticsService diagnostics, CancellationToken cancellationToken) =>
    Results.Ok(await diagnostics.GetSummaryAsync(cancellationToken)));
diagnosticsAdmin.MapPost("/support-bundles", async (string? mode, ISupportBundleService bundles, CancellationToken cancellationToken) =>
{
    try { return Results.Ok(await bundles.CreateAsync(mode ?? "standard", cancellationToken)); }
    catch (SupportBundleBusyException) { return SafeProblem(StatusCodes.Status409Conflict, "BACKUP_OPERATION_LOCKED", "A support bundle operation is already running.", CorrelationId.Create()); }
    catch (ArgumentException) { return SafeProblem(StatusCodes.Status400BadRequest, "INVALID_REQUEST", "Unsupported support bundle mode.", CorrelationId.Create()); }
});
diagnosticsAdmin.MapGet("/support-bundles/{id:guid}", (Guid id, ISupportBundleService bundles) =>
    bundles.Get(id) is { } result ? Results.Ok(result) : Results.NotFound());
diagnosticsAdmin.MapGet("/support-bundles/{id:guid}/download", (Guid id, ISupportBundleService bundles) =>
{
    var stream = bundles.Open(id, out var fileName);
    return stream is null ? Results.NotFound() : Results.File(stream, "application/zip", fileName, enableRangeProcessing: true);
});

app.MapHub<GameHub>("/hubs/game").RequireRateLimiting("room-operations");
app.MapRoomEndpoints();
app.MapContentEndpoints();
app.MapAdminContentEndpoints();
app.MapMediaEndpoints();

app.Logger.LogInformation(
    "Starting PartyGame.Api in {Environment}; game hub: {GameHubPath}",
    app.Environment.EnvironmentName,
    "/hubs/game");

app.Run();

static int PhotoStatus(string code) => code is "photo_answer_file_missing" or "photo_answer_file_empty" or "photo_answer_file_too_large" or "photo_answer_invalid_content_type" or "photo_answer_invalid_image" or "photo_answer_dimensions_too_small" or "photo_answer_dimensions_too_large"
    ? StatusCodes.Status400BadRequest
    : code is "photo_answer_not_found" ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict;

static int DrawingStatus(string code) => code is "drawing_answer_file_missing" or "drawing_answer_file_empty" or "drawing_answer_file_too_large" or "drawing_answer_invalid_content_type" or "drawing_answer_invalid_image" or "drawing_answer_dimensions_too_small" or "drawing_answer_dimensions_too_large" or "drawing_answer_blank"
    ? StatusCodes.Status400BadRequest
    : code is "drawing_answer_not_found" ? StatusCodes.Status404NotFound
    : code is "drawing_answer_storage_failed" ? StatusCodes.Status503ServiceUnavailable
    : StatusCodes.Status409Conflict;

static string RoomRatePartition(PathString path)
{
    var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries) ?? [];
    return segments.Length >= 3 && string.Equals(segments[0], "api", StringComparison.OrdinalIgnoreCase) &&
           string.Equals(segments[1], "rooms", StringComparison.OrdinalIgnoreCase)
        ? segments[2].ToUpperInvariant()
        : path.Value ?? "unknown";
}

static IResult SafeProblem(int statusCode, string errorCode, string message, string correlationId) =>
    Results.Problem(statusCode: statusCode, title: message, extensions: ErrorExtensions(errorCode, correlationId));

static Dictionary<string, object?> ErrorExtensions(string errorCode, string correlationId) => new()
{
    ["code"] = errorCode,
    ["errorCode"] = errorCode,
    ["correlationId"] = correlationId
};

public sealed record HealthResponse(
    string Status,
    string Service,
    string Version,
    DateTimeOffset UtcTime);

public sealed record SystemVersionResponse(
    string Version,
    string InformationalVersion,
    string CommitHash,
    string BuildTimestampUtc,
    string Environment,
    string ApiContractVersion,
    string SignalRContractVersion,
    string DatabaseSchemaVersion,
    string BackupFormatVersion,
    string SupportBundleFormatVersion,
    string DisplayVersion,
    string AdminVersion,
    string? IosClientVersion,
    string? MinimumSupportedIosVersion);

public partial class Program;
