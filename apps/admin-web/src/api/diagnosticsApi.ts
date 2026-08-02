import { apiUrl } from './apiConfig';
import { clearOperatorToken, getOperatorToken } from './operatorSession';

export type DiagnosticsSummary = {
  version: { applicationVersion: string; commitHash: string; environment: string };
  readiness: { status: string; database: string; schema: string; mediaStorage: string };
  uptimeSeconds: number;
  connections: { activeRooms: number; activeSignalRConnections: number };
};
export type SupportBundleStatus = { id: string; status: string; fileName: string; createdAtUtc: string; sizeBytes: number; mode: string; errorCode?: string };

async function request(path: string, init?: RequestInit): Promise<Response> {
  const token = getOperatorToken();
  const response = await fetch(apiUrl(path), { ...init, headers: { Accept: 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init?.headers } });
  if (response.status === 401) clearOperatorToken();
  if (!response.ok) throw new Error(`Backend zwrócił HTTP ${response.status}.`);
  return response;
}

export const diagnosticsApi = {
  summary: async () => (await request('/api/admin/diagnostics/summary')).json() as Promise<DiagnosticsSummary>,
  createBundle: async (mode: 'minimal' | 'standard' | 'extended' = 'standard') => (await request(`/api/admin/diagnostics/support-bundles?mode=${mode}`, { method: 'POST' })).json() as Promise<SupportBundleStatus>,
  status: async (id: string) => (await request(`/api/admin/diagnostics/support-bundles/${encodeURIComponent(id)}`)).json() as Promise<SupportBundleStatus>,
  download: async (id: string) => {
    const response = await request(`/api/admin/diagnostics/support-bundles/${encodeURIComponent(id)}/download`);
    return { blob: await response.blob(), fileName: response.headers.get('content-disposition')?.match(/filename="?([^";]+)"?/)?.[1] ?? 'partygame-support.zip' };
  },
};
