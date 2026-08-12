import { describe, expect, it } from 'vitest';
import { playerJoinUrl } from './playerJoinUrl';

describe('playerJoinUrl', () => {
  it('uses the configured public host, encoded room code, and no session secret', () => {
    const url = playerJoinUrl('A B&', 'https://party.example/display/', 'http://ignored.example');
    expect(url).toBe('https://party.example/play/?room=A+B%26');
    expect(url).toContain('/play/?room=');
    expect(url).not.toMatch(/playerId|reconnect|token|admin|operator/i);
  });
});
