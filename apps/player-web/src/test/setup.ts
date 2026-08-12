import '@testing-library/jest-dom/vitest';
import { configureRuntimeConfig } from '../api/apiConfig';

const values = new Map<string, string>();
Object.defineProperty(globalThis, 'localStorage', {
  configurable: true,
  value: {
    getItem: (key: string) => values.get(key) ?? null,
    setItem: (key: string, value: string) => values.set(key, value),
    removeItem: (key: string) => values.delete(key),
    clear: () => values.clear(),
  },
});

configureRuntimeConfig({ apiBaseUrl: 'http://test-api.local', signalRHubUrl: 'http://test-api.local/hubs/game', publicBaseUrl: '/play/', applicationVersion: 'test' });
