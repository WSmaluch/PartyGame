import { apiUrl } from './apiConfig';
import type { HealthResponse } from './types';

let latestCorrelationId = '';
export function lastCorrelationId(): string { return latestCorrelationId; }

export class HealthApiError extends Error {
  readonly kind: 'cancelled' | 'http' | 'invalid-response' | 'network';

  constructor(
    message: string,
    kind: 'cancelled' | 'http' | 'invalid-response' | 'network',
  ) {
    super(message);
    this.name = 'HealthApiError';
    this.kind = kind;
  }
}

function isHealthResponse(value: unknown): value is HealthResponse {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Record<string, unknown>;
  return (
    typeof candidate.status === 'string' &&
    typeof candidate.service === 'string' &&
    typeof candidate.version === 'string' &&
    typeof candidate.utcTime === 'string' &&
    !Number.isNaN(Date.parse(candidate.utcTime))
  );
}

export async function getHealth(signal?: AbortSignal): Promise<HealthResponse> {
  try {
    const response = await fetch(apiUrl('/health'), {
      method: 'GET',
      headers: { Accept: 'application/json' },
      signal,
    });
    if (!response.ok) {
      throw new HealthApiError(
        `Backend zwrócił HTTP ${response.status}.`,
        'http',
      );
    }
    latestCorrelationId = response.headers.get('X-Correlation-ID') ?? latestCorrelationId;
    const body: unknown = await response.json();
    if (!isHealthResponse(body)) {
      throw new HealthApiError(
        'Backend zwrócił niepoprawny format danych.',
        'invalid-response',
      );
    }
    return body;
  } catch (error) {
    if (error instanceof HealthApiError) throw error;
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw new HealthApiError('Żądanie anulowano.', 'cancelled');
    }
    throw new HealthApiError('Nie można połączyć się z backendem.', 'network');
  }
}
