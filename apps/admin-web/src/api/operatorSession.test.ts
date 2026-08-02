import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  clearOperatorToken,
  getOperatorToken,
  setOperatorToken,
  subscribeOperatorToken,
} from './operatorSession';

afterEach(() => clearOperatorToken());

describe('operatorSession', () => {
  it('keeps the token only in memory and clears it on logout', () => {
    const listener = vi.fn();
    const unsubscribe = subscribeOperatorToken(listener);
    setOperatorToken('operator-token');
    expect(getOperatorToken()).toBe('operator-token');
    expect(window.localStorage.getItem('operatorToken')).toBeNull();
    clearOperatorToken();
    expect(getOperatorToken()).toBeUndefined();
    expect(listener).toHaveBeenLastCalledWith(undefined);
    unsubscribe();
  });
});
