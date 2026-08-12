import { apiUrl } from './apiConfig';
import type { RoomAccessResponse } from './types';

export class PlayerApiError extends Error {
  readonly kind: 'not-found' | 'started' | 'validation' | 'network' | 'server';

  constructor(kind: 'not-found' | 'started' | 'validation' | 'network' | 'server') {
    super(kind);
    this.kind = kind;
  }
}

export async function joinRoom(roomCode: string, nickname: string): Promise<RoomAccessResponse> {
  let response: Response;
  try {
    response = await fetch(apiUrl(`/api/rooms/${encodeURIComponent(roomCode)}/players`), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
      body: JSON.stringify({ nickname }),
    });
  } catch {
    throw new PlayerApiError('network');
  }
  if (!response.ok) throw new PlayerApiError(errorKind(response.status));
  try {
    return (await response.json()) as RoomAccessResponse;
  } catch {
    throw new PlayerApiError('server');
  }
}

function errorKind(status: number): PlayerApiError['kind'] {
  if (status === 404) return 'not-found';
  if (status === 400) return 'validation';
  if (status === 409 || status === 422) return 'started';
  return 'server';
}
