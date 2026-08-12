import type { PlayerSession } from '../api/types';

const storageKey = 'partygame.player.session';

export function savePlayerSession(session: PlayerSession): void {
  localStorage.setItem(storageKey, JSON.stringify(session));
}

export function loadPlayerSession(): PlayerSession | undefined {
  try {
    const raw = localStorage.getItem(storageKey);
    if (!raw) return undefined;
    const value = JSON.parse(raw) as Partial<PlayerSession>;
    if (typeof value.roomCode !== 'string' || typeof value.playerId !== 'string' ||
      typeof value.reconnectToken !== 'string' || typeof value.nickname !== 'string') return undefined;
    return { roomCode: value.roomCode, playerId: value.playerId, reconnectToken: value.reconnectToken, nickname: value.nickname };
  } catch { return undefined; }
}

export function clearPlayerSession(): void {
  localStorage.removeItem(storageKey);
}
