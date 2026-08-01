export const apiConfig = {
  baseUrl: '',
  signalRBaseUrl: '',
  publicAppUrl: '',
  buildVersion: '',
};

export type RuntimeConfig = {
  apiBaseUrl: string;
  signalRBaseUrl?: string;
  publicAppUrl: string;
  buildVersion: string;
};

export function configureApiConfig(config: RuntimeConfig): void {
  apiConfig.baseUrl = normalizeHttpUrl(config.apiBaseUrl, 'apiBaseUrl');
  apiConfig.signalRBaseUrl = normalizeHttpUrl(
    config.signalRBaseUrl ?? config.apiBaseUrl,
    'signalRBaseUrl',
  );
  apiConfig.publicAppUrl = normalizeHttpUrl(
    config.publicAppUrl,
    'publicAppUrl',
  );
  apiConfig.buildVersion = nonEmpty(config.buildVersion, 'buildVersion');
}

export function parseRuntimeConfig(value: unknown): RuntimeConfig {
  if (value === null || typeof value !== 'object')
    throw new Error('Runtime configuration must be a JSON object.');
  const config = value as Record<string, unknown>;
  const parsed: RuntimeConfig = {
    apiBaseUrl: stringValue(config.apiBaseUrl, 'apiBaseUrl'),
    publicAppUrl: stringValue(config.publicAppUrl, 'publicAppUrl'),
    buildVersion: stringValue(config.buildVersion, 'buildVersion'),
  };
  if (config.signalRBaseUrl !== undefined)
    parsed.signalRBaseUrl = stringValue(
      config.signalRBaseUrl,
      'signalRBaseUrl',
    );
  configureApiConfig(parsed);
  return parsed;
}

export async function loadRuntimeConfig(
  fetchImpl: typeof fetch = fetch,
): Promise<RuntimeConfig> {
  const developmentApiBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim();
  if (developmentApiBaseUrl) {
    return parseRuntimeConfig({
      apiBaseUrl: developmentApiBaseUrl,
      signalRBaseUrl:
        import.meta.env.VITE_SIGNALR_BASE_URL?.trim() || developmentApiBaseUrl,
      publicAppUrl:
        import.meta.env.VITE_PUBLIC_APP_URL?.trim() || window.location.origin,
      buildVersion: import.meta.env.VITE_BUILD_VERSION?.trim() || 'development',
    });
  }

  let response: Response;
  try {
    response = await fetchImpl('/config.json', { cache: 'no-store' });
  } catch {
    throw new Error(
      'Runtime configuration could not be loaded from /config.json.',
    );
  }
  if (!response.ok)
    throw new Error(
      `Runtime configuration request failed with HTTP ${response.status}.`,
    );
  return parseRuntimeConfig(await response.json());
}

export function apiUrl(path: string): string {
  if (!apiConfig.baseUrl)
    throw new Error('Runtime configuration has not been loaded.');
  return `${apiConfig.baseUrl}${path.startsWith('/') ? path : `/${path}`}`;
}

function stringValue(value: unknown, name: string): string {
  return nonEmpty(typeof value === 'string' ? value : '', name);
}

function nonEmpty(value: string, name: string): string {
  if (!value.trim())
    throw new Error(`Runtime configuration field '${name}' is required.`);
  return value.trim();
}

function normalizeHttpUrl(value: string, name: string): string {
  const normalized = nonEmpty(value, name).replace(/\/$/, '');
  let url: URL;
  try {
    url = new URL(normalized);
  } catch {
    throw new Error(
      `Runtime configuration field '${name}' must be an absolute http or https URL.`,
    );
  }
  if (!['http:', 'https:'].includes(url.protocol))
    throw new Error(
      `Runtime configuration field '${name}' must be an absolute http or https URL.`,
    );
  return normalized;
}
