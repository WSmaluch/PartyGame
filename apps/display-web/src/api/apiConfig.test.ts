import { describe, expect, it } from 'vitest';
import { apiConfig, configureApiConfig, parseRuntimeConfig } from './apiConfig';

describe('runtime configuration', () => {
  it('accepts explicit API, SignalR, public URL and build version', () => {
    const config = parseRuntimeConfig({
      apiBaseUrl: 'https://api.partygame.test/',
      signalRBaseUrl: 'https://signalr.partygame.test/',
      publicAppUrl: 'https://display.partygame.test/display',
      buildVersion: '0.8.1-abcd',
    });

    expect(config.buildVersion).toBe('0.8.1-abcd');
    expect(apiConfig.baseUrl).toBe('https://api.partygame.test');
    expect(apiConfig.signalRBaseUrl).toBe('https://signalr.partygame.test');
  });

  it('rejects missing configuration instead of falling back to localhost', () => {
    expect(() =>
      parseRuntimeConfig({
        apiBaseUrl: '',
        publicAppUrl: '',
        buildVersion: '',
      }),
    ).toThrow("'apiBaseUrl' is required");
  });

  it('derives SignalR base URL from the explicit API URL', () => {
    configureApiConfig({
      apiBaseUrl: 'http://192.168.1.5:5050',
      publicAppUrl: 'http://192.168.1.5:5173/display',
      buildVersion: 'dev',
    });
    expect(apiConfig.signalRBaseUrl).toBe('http://192.168.1.5:5050');
  });

  it('accepts an origin-relative single-origin LAN runtime contract', () => {
    parseRuntimeConfig({
      apiBaseUrl: '/',
      signalRHubUrl: '/hubs/game',
      publicBaseUrl: '/display/',
      applicationVersion: '0.8.2-lan',
    });
    expect(apiConfig.baseUrl).toBe('/');
    expect(apiConfig.signalRHubUrl).toBe('/hubs/game');
  });
});
