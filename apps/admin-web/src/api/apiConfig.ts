const configuredBaseUrl = import.meta.env.VITE_API_BASE_URL?.trim();

export const apiConfig = {
  baseUrl: (configuredBaseUrl || 'http://localhost:5050').replace(/\/$/, ''),
} as const;

export function apiUrl(path: string): string {
  return `${apiConfig.baseUrl}${path.startsWith('/') ? path : `/${path}`}`;
}
