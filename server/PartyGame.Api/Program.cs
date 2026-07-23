using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PartyGame.Api.Endpoints;
using PartyGame.Api.Hubs;
using PartyGame.Domain.Content;
using PartyGame.Domain.Rooms;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Media;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddProblemDetails();
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
builder.Services.AddOptions<MediaOptions>()
    .Bind(builder.Configuration.GetSection(MediaOptions.SectionName))
    .Validate(options => options.Provider == "LocalFileSystem", "Only the LocalFileSystem media provider is available in this release.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.RootPath), "MediaStorage:RootPath is required.")
    .Validate(options => options.UntrackedFileCleanupGracePeriodMinutes > 0, "MediaStorage:UntrackedFileCleanupGracePeriodMinutes must be greater than zero.")
    .ValidateOnStart();
builder.Services.Configure<DrawingMediaOptions>(builder.Configuration.GetSection(DrawingMediaOptions.SectionName));
builder.Services.AddSingleton<IMediaStorage, LocalMediaStorage>();
builder.Services.AddSingleton<ILocalMediaFileCatalog, LocalMediaFileCatalog>();
builder.Services.AddScoped<IProfilePhotoCleanupService, ProfilePhotoCleanupService>();
builder.Services.AddScoped<IOrphanedGameMediaCleanupService, OrphanedGameMediaCleanupService>();
builder.Services.AddScoped<IUntrackedMediaFileCleanupService, UntrackedMediaFileCleanupService>();
builder.Services.AddSingleton<PartyGame.Api.Contracts.IPhotoMediaUrlProvider, PartyGame.Api.Contracts.PhotoMediaUrlProvider>();
builder.Services.AddSingleton<RoomNotifier>();
builder.Services.AddDbContext<PartyGameDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("PartyGame")));

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
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

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        IResult result = exception switch
        {
            DomainValidationException validation => Results.ValidationProblem(validation.Errors, statusCode: StatusCodes.Status400BadRequest),
            RoomNotFoundException or PlayerNotFoundException => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: exception.Message),
            InvalidPlayerTokenException => Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: exception.Message),
            PhotoAnswerException photo => Results.Problem(statusCode: PhotoStatus(photo.Code), title: exception.Message, extensions: new Dictionary<string, object?> { ["code"] = photo.Code }),
            DrawingAnswerException drawing => Results.Problem(statusCode: DrawingStatus(drawing.Code), title: exception.Message, extensions: new Dictionary<string, object?> { ["code"] = drawing.Code }),
            RoomConflictException or RoomCodeGenerationException => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: exception.Message),
            _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "An unexpected error occurred.")
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

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
    await dbContext.Database.MigrateAsync();
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

app.MapGet("/health", (IGameClock clock) =>
    Results.Ok(new HealthResponse(
        "ok",
        "PartyGame.Api",
        Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
        clock.UtcNow)))
    .WithName("GetHealth")
    .Produces<HealthResponse>(StatusCodes.Status200OK);

app.MapHub<GameHub>("/hubs/game");
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

public sealed record HealthResponse(
    string Status,
    string Service,
    string Version,
    DateTimeOffset UtcTime);

public partial class Program;
