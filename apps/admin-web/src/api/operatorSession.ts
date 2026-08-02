const listeners = new Set<(token: string | undefined) => void>();
let operatorToken: string | undefined;

export function getOperatorToken(): string | undefined {
  return operatorToken;
}

export function setOperatorToken(token: string): void {
  operatorToken = token.trim() || undefined;
  notify();
}

export function clearOperatorToken(): void {
  operatorToken = undefined;
  notify();
}

export function subscribeOperatorToken(listener: (token: string | undefined) => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

function notify(): void {
  listeners.forEach((listener) => listener(operatorToken));
}
