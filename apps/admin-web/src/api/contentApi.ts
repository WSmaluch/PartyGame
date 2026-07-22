import { adminContentApi, type ContentPackage } from './adminContentApi';

export type { ContentPackage };

export function getContentPackages(signal?: AbortSignal) {
  void signal;
  return adminContentApi.listPackages();
}

export function createDraft(packageVersionId: string) {
  return adminContentApi.createDraft(packageVersionId);
}
