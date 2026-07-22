import { apiUrl } from './apiConfig';

export type PackageStatus = 'Draft' | 'Published' | 'Archived';
export type QuestionType = 'PlayerSelection' | 'TextAnswer' | 'PhotoAnswer' | 'DrawingAnswer';
export type ContentPackage = { id: string; logicalPackageId: string; version: number; key: string; namePl: string; nameEn: string; descriptionPl?: string; descriptionEn?: string; status: PackageStatus; isActive: boolean; categoryCount: number; questionCount: number; questionCountByType?: Record<string, number>; publishedAtUtc?: string | null; archivedAtUtc?: string | null; updatedAtUtc?: string; concurrencyToken: string };
export type Category = { id: string; packageId: string; key: string; namePl: string; nameEn: string; descriptionPl: string; descriptionEn: string; isActive: boolean; sortOrder: number; questionCount: number; concurrencyToken: string };
export type QuestionValidationError = { path: string; code: string; message: string };
export type Question = { id: string; packageId: string; categoryId: string; categoryKey: string; categoryNamePl: string; key: string; questionType: QuestionType; type: QuestionType; textPl: string; textEn: string; isEnabled: boolean; isActive: boolean; minimumPlayers: number; sortOrder: number; createdAtUtc: string; updatedAtUtc: string; concurrencyToken: string; validationErrors: QuestionValidationError[] };
export type Page<T> = { items: T[]; page: number; pageSize: number; totalItems: number; totalPages: number; packageConcurrencyToken: string };
export type QuestionListQuery = { search?: string; categoryId?: string; questionType?: QuestionType; isEnabled?: boolean; missingTranslation?: boolean; validationErrors?: boolean; page?: number; pageSize?: number; sort?: string };
export type QuestionMutation = { question: Question; packageConcurrencyToken: string };
export type QuestionDetail = { question: Question; packageConcurrencyToken: string; packageStatus: PackageStatus };
export type CategoryMutation = { category: Category; packageConcurrencyToken: string };
export class AdminContentApiError extends Error {
  readonly status: number;
  readonly body?: unknown;
  constructor(status: number, message: string, body?: unknown) {
    super(message);
    this.status = status;
    this.body = body;
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(apiUrl(path), { ...init, headers: { Accept: 'application/json', ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...init?.headers } });
  const body: unknown = await response.json().catch(() => undefined);
  if (!response.ok) {
    const message = typeof body === 'object' && body && 'message' in body && typeof body.message === 'string' ? body.message : `Backend zwrócił HTTP ${response.status}.`;
    throw new AdminContentApiError(response.status, message, body);
  }
  return body as T;
}
const json = (value: unknown) => JSON.stringify(value);
export const adminContentApi = {
  listPackages: () => request<ContentPackage[]>('/api/admin/content-packages'),
  getPackage: (id: string) => request<ContentPackage>(`/api/admin/content-packages/${id}`),
  createPackage: (value: Partial<ContentPackage>) => request<ContentPackage>('/api/admin/content-packages', { method: 'POST', body: json(value) }),
  updatePackage: (id: string, value: Partial<ContentPackage>) => request<ContentPackage>(`/api/admin/content-packages/${id}`, { method: 'PATCH', body: json(value) }),
  createDraft: (id: string) => request<ContentPackage>(`/api/admin/content-packages/${id}/create-draft`, { method: 'POST' }),
  publish: (id: string, concurrencyToken: string) => request<ContentPackage>(`/api/admin/content-packages/${id}/publish`, { method: 'POST', body: json({ concurrencyToken }) }),
  archive: (id: string, concurrencyToken: string) => request<ContentPackage>(`/api/admin/content-packages/${id}/archive`, { method: 'POST', body: json({ concurrencyToken }) }),
  createCategory: (packageId: string, value: Partial<Category> & { packageConcurrencyToken: string }) => request<CategoryMutation>(`/api/admin/content-packages/${packageId}/categories`, { method: 'POST', body: json(value) }),
  listCategories: (packageId: string) => request<{ items: Category[]; packageConcurrencyToken: string }>(`/api/admin/content-packages/${packageId}/categories`),
  updateCategory: (packageId: string, id: string, value: Partial<Category> & { concurrencyToken: string; packageConcurrencyToken: string }) => request<CategoryMutation>(`/api/admin/content-packages/${packageId}/categories/${id}`, { method: 'PATCH', body: json(value) }),
  deleteCategory: (packageId: string, id: string, mode: 'reject' | 'deleteQuestions' | 'moveQuestions', concurrencyToken: string, packageConcurrencyToken: string, targetCategoryId?: string) => request<{ success: boolean; packageConcurrencyToken: string }>(`/api/admin/content-packages/${packageId}/categories/${id}?${new URLSearchParams({ mode, concurrencyToken, packageConcurrencyToken, ...(targetCategoryId ? { targetCategoryId } : {}) })}`, { method: 'DELETE' }),
  reorderCategories: (packageId: string, packageConcurrencyToken: string, items: { id: string; sortOrder: number }[]) => request<{ items: Category[]; packageConcurrencyToken: string }>(`/api/admin/content-packages/${packageId}/categories/reorder`, { method: 'POST', body: json({ packageConcurrencyToken, items }) }),
  listQuestions: (packageId: string, query: QuestionListQuery = {}) => { const params = new URLSearchParams(); Object.entries(query).forEach(([key, value]) => { if (value !== undefined && value !== '') params.set(key, String(value)); }); return request<Page<Question>>(`/api/admin/content-packages/${packageId}/questions${params.size ? `?${params}` : ''}`); },
  getQuestion: (packageId: string, id: string) => request<QuestionDetail>(`/api/admin/content-packages/${packageId}/questions/${id}`),
  createQuestion: (packageId: string, value: Partial<Question>) => request<Question>(`/api/admin/content-packages/${packageId}/questions`, { method: 'POST', body: json(value) }),
  updateQuestion: (packageId: string, id: string, value: Partial<Question> & { concurrencyToken: string; packageConcurrencyToken: string }) => request<QuestionMutation>(`/api/admin/content-packages/${packageId}/questions/${id}`, { method: 'PATCH', body: json(value) }),
  deleteQuestion: (packageId: string, id: string, concurrencyToken: string, packageConcurrencyToken: string) => request<{ success: boolean; packageConcurrencyToken: string }>(`/api/admin/content-packages/${packageId}/questions/${id}?${new URLSearchParams({ concurrencyToken, packageConcurrencyToken })}`, { method: 'DELETE' }),
  duplicateQuestion: (packageId: string, id: string, concurrencyToken: string, packageConcurrencyToken: string) => request<QuestionMutation>(`/api/admin/content-packages/${packageId}/questions/${id}/duplicate`, { method: 'POST', body: json({ concurrencyToken, packageConcurrencyToken }) }),
  reorderQuestions: (packageId: string, packageConcurrencyToken: string, items: { id: string; sortOrder: number }[]) => request<{ items: Question[]; packageConcurrencyToken: string }>(`/api/admin/content-packages/${packageId}/questions/reorder`, { method: 'POST', body: json({ packageConcurrencyToken, items }) }),
};
