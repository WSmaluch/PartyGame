import { beforeEach, describe, expect, it } from 'vitest';
import { submissionIdentity } from './submissionIdentity';

describe('submissionIdentity', () => {
  beforeEach(() => sessionStorage.clear());

  it('reuses a UUID for retries without storing session credentials', () => {
    const first = submissionIdentity('AB12', 'player-id', 'question-id', 'text-answer');
    const retry = submissionIdentity('AB12', 'player-id', 'question-id', 'text-answer');
    expect(retry).toBe(first); expect(first).toMatch(/^[\da-f-]{36}$/i);
    expect(Object.values(sessionStorage)).not.toContain('reconnect-token');
  });
});
