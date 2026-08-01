import '@testing-library/jest-dom/vitest';
import { configureApiConfig } from '../api/apiConfig';

// Tests are not an application startup path: make their server explicit rather
// than reintroducing an implicit localhost fallback in production code.
configureApiConfig({
  apiBaseUrl: 'http://test-api.local',
  publicAppUrl: 'http://test-display.local/display',
  buildVersion: 'test',
});
