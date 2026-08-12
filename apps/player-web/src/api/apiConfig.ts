export type RuntimeConfig = {
  apiBaseUrl: string;
  signalRHubUrl: string;
  publicBaseUrl: string;
  applicationVersion: string;
  signalRBaseUrl?: string;
};

let runtimeConfig: RuntimeConfig | undefined;

export function parseRuntimeConfig(value: unknown): RuntimeConfig {
  if (value === null || typeof value !== 'object')
    throw new Error('Runtime configuration must be a JSON object.');
  const config = value as Record<string, unknown>;
  const parsed: RuntimeConfig = {
    apiBaseUrl: normalizeBaseUrl(stringValue(config.apiBaseUrl, 'apiBaseUrl'), 'apiBaseUrl'),
    signalRHubUrl: normalizeBaseUrl(
      stringValue(config.signalRHubUrl ?? config.signalRBaseUrl, 'signalRHubUrl'),
      'signalRHubUrl',
    ),
    publicBaseUrl: normalizePublicBaseUrl(stringValue(config.publicBaseUrl, 'publicBaseUrl')),
    applicationVersion: stringValue(config.applicationVersion, 'applicationVersion'),
  };
  if (config.signalRBaseUrl !== undefined)
    parsed.signalRBaseUrl = normalizeBaseUrl(stringValue(config.signalRBaseUrl, 'signalRBaseUrl'), 'signalRBaseUrl');
  return parsed;
}

export function configureRuntimeConfig(config: RuntimeConfig): void {
  runtimeConfig = config;
}

export async function loadRuntimeConfig(fetchImpl: typeof fetch = fetch): Promise<RuntimeConfig> {
  const developmentApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim();
  const config = developmentApiBaseUrl
    ? parseRuntimeConfig({
        apiBaseUrl: developmentApiBaseUrl,
        signalRHubUrl: import.meta.env.VITE_SIGNALR_HUB_URL?.trim() || `${developmentApiBaseUrl}/hubs/game`,
        publicBaseUrl: import.meta.env.VITE_PUBLIC_BASE_URL?.trim() || '/play/',
        applicationVersion: import.meta.env.VITE_BUILD_VERSION?.trim() || 'development',
      })
    : await loadProductionConfig(fetchImpl);
  configureRuntimeConfig(config);
  return config;
}

async function loadProductionConfig(fetchImpl: typeof fetch): Promise<RuntimeConfig> {
  let response: Response;
  try {
    response = await fetchImpl(`${applicationBasePath()}config.json`, { cache: 'no-store' });
  } catch {
    throw new Error('Runtime configuration could not be loaded.');
  }
  if (!response.ok) throw new Error('Runtime configuration could not be loaded.');
  try {
    return parseRuntimeConfig(await response.json());
  } catch {
    throw new Error('Runtime configuration is invalid.');
  }
}

export function apiUrl(path: string): string {
  const config = requireConfig();
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  return config.apiBaseUrl === '/' ? normalizedPath : `${config.apiBaseUrl}${normalizedPath}`;
}

export function signalRHubUrl(): string {
  return requireConfig().signalRHubUrl;
}

function requireConfig(): RuntimeConfig {
  if (!runtimeConfig) throw new Error('Runtime configuration has not been loaded.');
  return runtimeConfig;
}

function stringValue(value: unknown, name: string): string {
  if (typeof value !== 'string' || !value.trim())
    throw new Error(`Runtime configuration field '${name}' is required.`);
  return value.trim();
}

function normalizeBaseUrl(value: string, name: string): string {
  const normalized = value.replace(/\/$/, '');
  if (normalized === '') return '/';
  if (normalized.startsWith('/') && !normalized.startsWith('//')) return normalized;
  let url: URL;
  try { url = new URL(normalized); } catch { throw new Error(`Runtime configuration field '${name}' is invalid.`); }
  if (!['http:', 'https:'].includes(url.protocol)) throw new Error(`Runtime configuration field '${name}' is invalid.`);
  return normalized;
}

function normalizePublicBaseUrl(value: string): string {
  const normalized = normalizeBaseUrl(value, 'publicBaseUrl');
  return normalized === '/' ? '/' : `${normalized}/`;
}

function applicationBasePath(): string {
  // Vitest exposes '/' instead of Vite's configured base; the deployed app is
  // intentionally rooted at /play/ even when that test-time fallback is used.
  return import.meta.env.BASE_URL === '/' ? '/play/' : import.meta.env.BASE_URL;
}
