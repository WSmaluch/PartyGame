import { apiUrl } from './apiConfig';
import type { ProblemDetails, RoomSnapshot } from './types';

export class RoomApiError extends Error {
  readonly status?: number;

  constructor(message: string, status?: number) {
    super(message);
    this.name = 'RoomApiError';
    this.status = status;
  }
}

export async function getRoomSnapshot(
  roomCode: string,
  signal?: AbortSignal,
): Promise<RoomSnapshot> {
  try {
    const response = await fetch(apiUrl(`/api/rooms/${roomCode}`), {
      headers: { Accept: 'application/json' },
      signal,
    });
    if (!response.ok) {
      const problem = (await response.json().catch(() => undefined)) as
        | ProblemDetails
        | undefined;
      const validation = problem?.errors
        ? Object.values(problem.errors).flat()[0]
        : undefined;
      throw new RoomApiError(
        validation ?? problem?.detail ?? problem?.title ?? `Backend zwrócił HTTP ${response.status}.`,
        response.status,
      );
    }
    return (await response.json()) as RoomSnapshot;
  } catch (error) {
    if (error instanceof RoomApiError) throw error;
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new RoomApiError('Nie można połączyć się z backendem.');
  }
}

export function profilePhotoUrl(path?: string | null): string | undefined {
  return publicMediaUrl(path);
}

// Public game media is intentionally token-free: Display has no player session.
// Keep its URL resolution identical to REST requests under a path-prefixed deployment.
export function publicMediaUrl(path?: string | null): string | undefined {
  if (!path) return undefined;
  if (/^https?:\/\//i.test(path)) return path;
  return apiUrl(path);
}
