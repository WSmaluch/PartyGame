using System.Collections.Concurrent;

namespace PartyGame.Infrastructure.Rooms;

public sealed class RoomLockProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public SemaphoreSlim For(string roomCode) => _locks.GetOrAdd(roomCode, static _ => new SemaphoreSlim(1, 1));
}
