import { apiUrl } from './apiConfig';
import type { PlayerSession, ResumePlayerResponse, RoomAccessResponse, RoomSnapshot } from './types';

export class PlayerApiError extends Error {
  readonly kind: 'not-found' | 'started' | 'validation' | 'network' | 'server' | 'invalid-session';

  constructor(kind: 'not-found' | 'started' | 'validation' | 'network' | 'server' | 'invalid-session') {
    super(kind);
    this.kind = kind;
  }
}

export async function resumePlayer(session: PlayerSession): Promise<ResumePlayerResponse> {
  return requestJson<ResumePlayerResponse>(
    `/api/rooms/${encodeURIComponent(session.roomCode)}/players/${encodeURIComponent(session.playerId)}/resume`,
    { method: 'POST', headers: playerHeaders(session.reconnectToken) },
    true,
  );
}

export async function uploadProfilePhoto(session: PlayerSession, file: Blob): Promise<RoomSnapshot> {
  const form = new FormData();
  form.append('file', file, 'profile.jpg');
  return requestJson<RoomSnapshot>(
    `/api/rooms/${encodeURIComponent(session.roomCode)}/players/${encodeURIComponent(session.playerId)}/profile-photo`,
    { method: 'POST', headers: { 'X-Player-Token': session.reconnectToken }, body: form },
    false,
  );
}

export async function joinRoom(roomCode: string, nickname: string): Promise<RoomAccessResponse> {
  return requestJson<RoomAccessResponse>(`/api/rooms/${encodeURIComponent(roomCode)}/players`, {
    method: 'POST', headers: { 'Content-Type': 'application/json', Accept: 'application/json' }, body: JSON.stringify({ nickname }),
  }, false);
}

async function requestJson<T>(path: string, init: RequestInit, sessionRequest: boolean): Promise<T> {
  let response: Response;
  try {
    response = await fetch(apiUrl(path), init);
  } catch {
    throw new PlayerApiError('network');
  }
  if (!response.ok) throw new PlayerApiError(sessionRequest && (response.status === 401 || response.status === 403 || response.status === 404) ? 'invalid-session' : errorKind(response.status));
  try {
    return (await response.json()) as T;
  } catch {
    throw new PlayerApiError('server');
  }
}

function playerHeaders(token: string): HeadersInit { return { 'X-Player-Token': token, Accept: 'application/json' }; }

function errorKind(status: number): PlayerApiError['kind'] {
  if (status === 404) return 'not-found';
  if (status === 400) return 'validation';
  if (status === 409 || status === 422) return 'started';
  return 'server';
}
