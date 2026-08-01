import '@testing-library/jest-dom/vitest';
import { configureApiConfig } from '../api/apiConfig';

configureApiConfig({
  apiBaseUrl: 'http://test-api.local',
  publicAppUrl: 'http://test-admin.local/admin',
  buildVersion: 'test',
});
