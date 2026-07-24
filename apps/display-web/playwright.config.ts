import { defineConfig, devices } from '@playwright/test';

const artifactsDirectory = process.env.PLAYWRIGHT_ARTIFACTS_DIR;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: 1, // Sterujemy jedną grą równocześnie, E2E wymaga pokoju.
  reporter: [
    ['html', { open: 'never', outputFolder: artifactsDirectory }],
    ['line'],
  ],
  outputDir: process.env.PLAYWRIGHT_OUTPUT_DIR,
  use: {
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    baseURL: process.env.VITE_URL || 'http://localhost:5173',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
