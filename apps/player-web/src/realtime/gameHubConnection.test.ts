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
    await hub.attach(session); signalr.handlers.reconnecting?.(); signalr.handlers.reconnected?.(); await vi.waitFor(() => expect(signalr.connection.invoke.mock.calls.filter((call: unknown[]) => call[0] === 'AttachPlayer')).toHaveLength(2));
    expect(signalr.connection.invoke.mock.calls.filter((call: unknown[]) => call[0] === 'AttachPlayer').at(-1)).toEqual(['AttachPlayer', 'AB12', 'player', 'token']); expect(states).toContain('reconnecting'); expect(states.at(-1)).toBe('connected');
  });

  it('uses the server submission methods and does not put identity in a URL', async () => {
    const hub = new GameHubConnection(); const session = { roomCode: 'AB12', playerId: 'player', reconnectToken: 'token', nickname: 'Wojtek' };
    await hub.attach(session);
    await hub.submitPlayerSelection(session, 'other', 'question', 'selection-id');
    await hub.submitTextAnswer(session, 'answer', 'question', 'answer-id');
    await hub.submitTextAnswerVote(session, 'answer-id', 'question', 'vote-id');
    await hub.submitPhotoAnswerVote(session, 'photo-id', 'question', 'photo-vote-id');
    await hub.submitDrawingAnswerVote(session, 'drawing-id', 'question', 'drawing-vote-id');
    expect(signalr.connection.invoke).toHaveBeenCalledWith('SubmitPlayerSelectionWithSubmission', 'AB12', 'player', 'token', 'other', 'question', 'selection-id');
    expect(signalr.connection.invoke).toHaveBeenCalledWith('SubmitTextAnswerWithSubmission', 'AB12', 'player', 'token', 'answer', 'question', 'answer-id');
    expect(signalr.connection.invoke).toHaveBeenCalledWith('SubmitTextAnswerVoteWithSubmission', 'AB12', 'player', 'token', 'answer-id', 'question', 'vote-id');
    expect(signalr.connection.invoke).toHaveBeenCalledWith('SubmitPhotoAnswerVoteWithSubmission', 'AB12', 'player', 'token', 'question', 'photo-id', 'photo-vote-id');
    expect(signalr.connection.invoke).toHaveBeenCalledWith('SubmitDrawingAnswerVoteWithSubmission', 'AB12', 'player', 'token', 'question', 'drawing-id', 'drawing-vote-id');
  });
});
