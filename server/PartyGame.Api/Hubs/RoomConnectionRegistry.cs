using System.Collections.Concurrent;

namespace PartyGame.Api.Hubs;

public interface IRoomConnectionRegistry
{
    string? AttachPlayer(string connectionId, string roomCode, Guid playerId);
    string? AttachDisplay(string connectionId, string roomCode);
    bool IsActivePlayer(string connectionId, string roomCode, Guid playerId);
    string? GetActivePlayerConnection(Guid playerId);
    ConnectionAssignment? RemoveIfActive(string connectionId);
}

public enum ConnectionRole { Player, Display }
public sealed record ConnectionAssignment(string ConnectionId, string RoomCode, ConnectionRole Role, Guid? PlayerId);

public sealed class RoomConnectionRegistry : IRoomConnectionRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<string, ConnectionAssignment> _byConnection = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _playerConnections = [];
    private readonly Dictionary<string, string> _displayConnections = new(StringComparer.OrdinalIgnoreCase);

    public string? AttachPlayer(string connectionId, string roomCode, Guid playerId)
    {
        lock (_sync)
        {
            RemoveExistingRole(connectionId);
            _playerConnections.TryGetValue(playerId, out var previous);
            if (previous is not null)
            {
                _byConnection.Remove(previous);
            }
            _playerConnections[playerId] = connectionId;
            _byConnection[connectionId] = new(connectionId, roomCode, ConnectionRole.Player, playerId);
            return previous == connectionId ? null : previous;
        }
    }

    public string? AttachDisplay(string connectionId, string roomCode)
    {
        lock (_sync)
        {
            RemoveExistingRole(connectionId);
            _displayConnections.TryGetValue(roomCode, out var previous);
            if (previous is not null)
            {
                _byConnection.Remove(previous);
            }
            _displayConnections[roomCode] = connectionId;
            _byConnection[connectionId] = new(connectionId, roomCode, ConnectionRole.Display, null);
            return previous == connectionId ? null : previous;
        }
    }

    public bool IsActivePlayer(string connectionId, string roomCode, Guid playerId)
    {
        lock (_sync)
        {
            return _playerConnections.TryGetValue(playerId, out var activeConnection) &&
                   activeConnection == connectionId &&
                   _byConnection.TryGetValue(connectionId, out var assignment) &&
                   assignment.Role == ConnectionRole.Player &&
                   assignment.PlayerId == playerId &&
                   assignment.RoomCode.Equals(roomCode, StringComparison.OrdinalIgnoreCase);
        }
    }

    public string? GetActivePlayerConnection(Guid playerId)
    {
        lock (_sync) return _playerConnections.GetValueOrDefault(playerId);
    }

    public ConnectionAssignment? RemoveIfActive(string connectionId)
    {
        lock (_sync)
        {
            if (!_byConnection.Remove(connectionId, out var assignment))
            {
                return null;
            }
            if (assignment.Role == ConnectionRole.Player && assignment.PlayerId is { } playerId &&
                _playerConnections.TryGetValue(playerId, out var activePlayer) && activePlayer == connectionId)
            {
                _playerConnections.Remove(playerId);
                return assignment;
            }
            if (assignment.Role == ConnectionRole.Display &&
                _displayConnections.TryGetValue(assignment.RoomCode, out var activeDisplay) && activeDisplay == connectionId)
            {
                _displayConnections.Remove(assignment.RoomCode);
                return assignment;
            }
            return null;
        }
    }

    private void RemoveExistingRole(string connectionId)
    {
        if (!_byConnection.Remove(connectionId, out var existing))
        {
            return;
        }
        if (existing.Role == ConnectionRole.Player && existing.PlayerId is { } playerId &&
            _playerConnections.TryGetValue(playerId, out var playerConnection) && playerConnection == connectionId)
        {
            _playerConnections.Remove(playerId);
        }
        if (existing.Role == ConnectionRole.Display &&
            _displayConnections.TryGetValue(existing.RoomCode, out var displayConnection) && displayConnection == connectionId)
        {
            _displayConnections.Remove(existing.RoomCode);
        }
    }
}
