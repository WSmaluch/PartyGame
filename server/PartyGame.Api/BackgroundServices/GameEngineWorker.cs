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
            try
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
                    var roomService = scope.ServiceProvider.GetRequiredService<IRoomService>();
                    var room = await roomService.GetAsync(candidate.RoomCode, cancellationToken);

                    if (room != null)
                    {
                        // The stage transition and its public state version must be persisted
                        // together.  Persisting them separately can leave a newer game stage
                        // behind an unchanged stateVersion when SQLite rejects the second write.
                        // Realtime clients correctly ignore that stale-version snapshot and would
                        // therefore never render the actionable next stage.
                        room.PublicStateChanged(clock.UtcNow);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        await notifier.NotifyAsync(new RoomMutationResult(room, true, false), cancellationToken);
                        logger.LogDebug("Game timeout transition accepted for room {RoomCode}; state version {StateVersion}", room.Code, room.StateVersion);
                    }
                }
                }
                finally { roomLock.Release(); }
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A single corrupt or concurrently changed room must not starve the
                // rest of the timeout batch. The next interval retries safely.
                logger.LogError(exception, "GameEngineWorker failed to process room {RoomCode}; error code {ErrorCode}", candidate.RoomCode, "INTERNAL_ERROR");
            }
        }
    }
}
