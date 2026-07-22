import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: './e2e', retries: 0, workers: 1,
  use: { baseURL: process.env.ADMIN_E2E_BASE_URL ?? 'http://127.0.0.1:15174', trace: 'retain-on-failure', screenshot: 'only-on-failure', video: 'retain-on-failure' },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
});
