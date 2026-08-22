import { describe, expect, it, vi } from 'vitest';
import { loadRuntimeConfig, parseRuntimeConfig } from './apiConfig';

describe('runtime configuration', () => {
  it('loads the /play config file through Vite base path', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response(JSON.stringify({ apiBaseUrl: '/', signalRHubUrl: '/hubs/game', publicBaseUrl: '/play/', applicationVersion: '1.0.0' }), { status: 200 }));
    await expect(loadRuntimeConfig(fetchImpl)).resolves.toMatchObject({ publicBaseUrl: '/play/' });
    expect(fetchImpl).toHaveBeenCalledWith('/play/config.json', { cache: 'no-store' });
  });

  it('reports invalid configuration without a localhost fallback', () => {
    expect(() => parseRuntimeConfig({ apiBaseUrl: '', signalRHubUrl: '', publicBaseUrl: '', applicationVersion: '' })).toThrow("'apiBaseUrl' is required");
  });
});
