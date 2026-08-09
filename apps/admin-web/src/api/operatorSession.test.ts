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
    // Some headless clean-clone runners expose jsdom without a localStorage
    // implementation. The contract is that this module never writes to it.
    expect(window.localStorage?.getItem('operatorToken') ?? null).toBeNull();
    clearOperatorToken();
    expect(getOperatorToken()).toBeUndefined();
    expect(listener).toHaveBeenLastCalledWith(undefined);
    unsubscribe();
  });
});
