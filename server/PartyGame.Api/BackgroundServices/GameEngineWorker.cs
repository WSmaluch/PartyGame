using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PartyGame.Api.Hubs;
using PartyGame.Domain.Game;
using PartyGame.GameEngine;
using PartyGame.Infrastructure.Persistence;
using PartyGame.Infrastructure.Rooms;


namespace PartyGame.Api.BackgroundServices;

public sealed class GameEngineWorker(
    IServiceProvider serviceProvider,
    RoomLockProvider lockProvider,
    IGameClock clock,
    IOptions<PartyGame.Infrastructure.Rooms.GameFlowOptions> options,
    ILogger<GameEngineWorker> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMilliseconds(options.Value.WorkerIntervalMilliseconds);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTimeoutsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred in the GameEngineWorker.");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task ProcessTimeoutsAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        List<(string RoomCode, Guid SessionId)> sessionsToProcess;

        // Fetch candidates without holding any room locks
        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
            // SQLite persists DateTimeOffset as text and cannot translate ordering reliably.
            // Keep stage filtering in SQL and perform the deadline comparison in memory.
            var pending = await dbContext.GameSessions
                .Where(s => s.StageEndsAtUtc != null && s.Stage != GameStage.PausedForDisplay && s.Stage != GameStage.Completed)
                .Select(s => new { s.Room.Code, s.Id, s.StageEndsAtUtc })
                .ToListAsync(cancellationToken);
            sessionsToProcess = pending
                .Where(x => x.StageEndsAtUtc <= now)
                .Select(x => (x.Code, x.Id))
                .ToList();
        }

        foreach (var candidate in sessionsToProcess)
        {
            var roomLock = lockProvider.For(candidate.RoomCode);
            await roomLock.WaitAsync(cancellationToken);
            try
            {
                // Re-evaluate in a fresh scope under lock to ensure consistency
                await using var scope = serviceProvider.CreateAsyncScope();
                var stateMachine = scope.ServiceProvider.GetRequiredService<GameStateMachine>();
                var dbContext = scope.ServiceProvider.GetRequiredService<PartyGameDbContext>();
                var notifier = scope.ServiceProvider.GetRequiredService<RoomNotifier>();

                var changed = await stateMachine.ProcessTransitionAsync(candidate.SessionId, clock.UtcNow, cancellationToken);
                if (changed)
                {
                    await dbContext.SaveChangesAsync(cancellationToken);

                    var roomService = scope.ServiceProvider.GetRequiredService<IRoomService>();
                    var room = await roomService.GetAsync(candidate.RoomCode, cancellationToken);

                    if (room != null)
                    {
                        room.PublicStateChanged(clock.UtcNow);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        await notifier.NotifyAsync(new RoomMutationResult(room, true, false), cancellationToken);
                    }
                }
            }
            finally
            {
                roomLock.Release();
            }
        }
    }
}
