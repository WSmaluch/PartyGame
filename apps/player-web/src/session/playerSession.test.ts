import { afterEach, describe, expect, it } from 'vitest';
import { clearPlayerSession, loadPlayerSession, savePlayerSession } from './playerSession';

describe('player session storage', () => {
  afterEach(clearPlayerSession);
  it('persists only the player reconnect session', () => {
    savePlayerSession({ roomCode: 'AB12', playerId: 'player-id', reconnectToken: 'reconnect-token', nickname: 'Wojtek' });
    expect(loadPlayerSession()).toEqual({ roomCode: 'AB12', playerId: 'player-id', reconnectToken: 'reconnect-token', nickname: 'Wojtek' });
    const stored = localStorage.getItem('partygame.player.session') ?? '';
    expect(stored).not.toContain('operator');
    expect(stored).not.toContain('admin');
  });
});
