import { describe, expect, it, vi } from 'vitest';
import { GameHubConnection } from './gameHubConnection';

const signalr = vi.hoisted(() => {
  const handlers: Record<string, (() => void) | undefined> = {};
  const connection = {
    state: 'Disconnected', on: vi.fn(), onreconnecting: vi.fn((handler) => { handlers.reconnecting = handler; }), onreconnected: vi.fn((handler) => { handlers.reconnected = handler; }), onclose: vi.fn(),
    start: vi.fn(async () => { connection.state = 'Connected'; }), invoke: vi.fn(async () => ({ roomCode: 'AB12', phase: 'Lobby', stateVersion: 2, players: [] })),
  };
  return { connection, handlers };
});

vi.mock('@microsoft/signalr', () => ({
  HubConnectionState: { Connected: 'Connected', Disconnected: 'Disconnected' }, LogLevel: { Warning: 2 },
  HubConnectionBuilder: class HubConnectionBuilder { withUrl() { return this; } withAutomaticReconnect() { return this; } configureLogging() { return this; } build() { return signalr.connection; } },
}));

describe('GameHubConnection', () => {
  it('reattaches the same player after SignalR reconnects', async () => {
    const hub = new GameHubConnection(); const states: string[] = []; hub.subscribe((state) => states.push(state));
    const session = { roomCode: 'AB12', playerId: 'player', reconnectToken: 'token', nickname: 'Wojtek' };
    await hub.attach(session); signalr.handlers.reconnecting?.(); signalr.handlers.reconnected?.(); await vi.waitFor(() => expect(signalr.connection.invoke).toHaveBeenCalledTimes(2));
    expect(signalr.connection.invoke).toHaveBeenLastCalledWith('AttachPlayer', 'AB12', 'player', 'token'); expect(states).toContain('reconnecting'); expect(states.at(-1)).toBe('connected');
  });
});
