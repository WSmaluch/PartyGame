export const apiConfig = {
  baseUrl: '',
  signalRHubUrl: '',
  publicBaseUrl: '',
  applicationVersion: '',
  commitHash: '',
  apiContractVersion: '',
  signalRContractVersion: '',
  signalRBaseUrl: '',
  publicAppUrl: '',
  buildVersion: '',
};

export type RuntimeConfig = {
  apiBaseUrl: string;
  signalRHubUrl?: string;
  publicBaseUrl?: string;
  applicationVersion?: string;
  commitHash?: string;
  apiContractVersion?: string;
  signalRContractVersion?: string;
  // Legacy aliases keep artifacts built before 8.2 usable during a rolling update.
  signalRBaseUrl?: string;
  publicAppUrl?: string;
  buildVersion?: string;
};

export function configureApiConfig(config: RuntimeConfig): void {
  apiConfig.baseUrl = normalizeBaseUrl(config.apiBaseUrl, 'apiBaseUrl');
  apiConfig.signalRHubUrl = normalizeBaseUrl(
    config.signalRHubUrl ?? config.signalRBaseUrl ?? '/hubs/game',
    'signalRHubUrl',
  );
  apiConfig.publicBaseUrl = normalizeBaseUrl(
    config.publicBaseUrl ?? config.publicAppUrl ?? '',
    'publicBaseUrl',
  );
  apiConfig.applicationVersion = nonEmpty(
    config.applicationVersion ?? config.buildVersion ?? '',
    'applicationVersion',
  );
  apiConfig.commitHash = config.commitHash ?? 'development';
  apiConfig.apiContractVersion = config.apiContractVersion ?? '1';
  apiConfig.signalRContractVersion = config.signalRContractVersion ?? apiConfig.apiContractVersion;
  apiConfig.signalRBaseUrl = normalizeBaseUrl(
    config.signalRBaseUrl ?? config.apiBaseUrl,
    'signalRBaseUrl',
  );
  apiConfig.publicAppUrl = normalizeBaseUrl(
    config.publicAppUrl ?? config.publicBaseUrl ?? '',
    'publicAppUrl',
  );
  apiConfig.buildVersion = nonEmpty(
    config.buildVersion ?? config.applicationVersion ?? '',
    'buildVersion',
  );
}

export function parseRuntimeConfig(value: unknown): RuntimeConfig {
  if (value === null || typeof value !== 'object')
    throw new Error('Runtime configuration must be a JSON object.');
  const config = value as Record<string, unknown>;
  const parsed: RuntimeConfig = {
    apiBaseUrl: stringValue(config.apiBaseUrl, 'apiBaseUrl'),
    publicAppUrl: stringValue(
      config.publicAppUrl ?? config.publicBaseUrl,
      'publicAppUrl',
    ),
    buildVersion: stringValue(
      config.buildVersion ?? config.applicationVersion,
      'buildVersion',
    ),
  };
  if (config.signalRBaseUrl !== undefined)
    parsed.signalRBaseUrl = stringValue(
      config.signalRBaseUrl,
      'signalRBaseUrl',
    );
  if (config.signalRHubUrl !== undefined)
    parsed.signalRHubUrl = stringValue(config.signalRHubUrl, 'signalRHubUrl');
  if (config.publicBaseUrl !== undefined)
    parsed.publicBaseUrl = stringValue(config.publicBaseUrl, 'publicBaseUrl');
  if (config.applicationVersion !== undefined)
    parsed.applicationVersion = stringValue(
      config.applicationVersion,
      'applicationVersion',
    );
  if (config.commitHash !== undefined) parsed.commitHash = stringValue(config.commitHash, 'commitHash');
  if (config.apiContractVersion !== undefined) parsed.apiContractVersion = stringValue(config.apiContractVersion, 'apiContractVersion');
  if (config.signalRContractVersion !== undefined) parsed.signalRContractVersion = stringValue(config.signalRContractVersion, 'signalRContractVersion');
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
      signalRHubUrl:
        import.meta.env.VITE_SIGNALR_HUB_URL?.trim() || '/hubs/game',
      publicBaseUrl:
        import.meta.env.VITE_PUBLIC_BASE_URL?.trim() || window.location.origin,
      applicationVersion:
        import.meta.env.VITE_BUILD_VERSION?.trim() || 'development',
      commitHash: import.meta.env.VITE_COMMIT_HASH?.trim() || 'development',
      apiContractVersion: '1',
      signalRContractVersion: '1',
      signalRBaseUrl:
        import.meta.env.VITE_SIGNALR_BASE_URL?.trim() || developmentApiBaseUrl,
      publicAppUrl:
        import.meta.env.VITE_PUBLIC_APP_URL?.trim() || window.location.origin,
      buildVersion: import.meta.env.VITE_BUILD_VERSION?.trim() || 'development',
    });
  }

  let response: Response;
  try {
    response = await fetchImpl(`${import.meta.env.BASE_URL}config.json`, {
      cache: 'no-store',
    });
  } catch {
    throw new Error(
      'Runtime configuration could not be loaded from the application config.json.',
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
  const normalizedPath = path.startsWith('/') ? path : `/${path}`;
  return apiConfig.baseUrl === '/'
    ? normalizedPath
    : `${apiConfig.baseUrl}${normalizedPath}`;
}

export function signalRHubUrl(): string {
  if (!apiConfig.signalRHubUrl)
    throw new Error('Runtime configuration has not been loaded.');
  return apiConfig.signalRHubUrl;
}

function stringValue(value: unknown, name: string): string {
  return nonEmpty(typeof value === 'string' ? value : '', name);
}

function nonEmpty(value: string, name: string): string {
  if (!value.trim())
    throw new Error(`Runtime configuration field '${name}' is required.`);
  return value.trim();
}

function normalizeBaseUrl(value: string, name: string): string {
  const normalized = nonEmpty(value, name).replace(/\/$/, '');
  if (normalized === '') return '/';
  if (normalized.startsWith('/') && !normalized.startsWith('//'))
    return normalized;
  let url: URL;
  try {
    url = new URL(normalized);
  } catch {
    throw new Error(
      `Runtime configuration field '${name}' must be an absolute http(s) URL or an origin-relative path.`,
    );
  }
  if (!['http:', 'https:'].includes(url.protocol))
    throw new Error(
      `Runtime configuration field '${name}' must be an absolute http(s) URL or an origin-relative path.`,
    );
  return normalized;
}
