import { expect, test } from '@playwright/test';

test('loads the deployed Display through its LAN origin and runtime config', async ({
  page,
}) => {
  await page.goto('/display/');
  await expect(page.getByText('Połącz ekran')).toBeVisible();

  const config = await page.evaluate(async () => {
    const response = await fetch('/display/config.json', { cache: 'no-store' });
    return { ok: response.ok, body: await response.text() };
  });
  expect(config.ok).toBe(true);
  expect(config.body).not.toContain('localhost');
  expect(config.body).not.toContain('127.0.0.1');
  expect(config.body).toContain('"signalRHubUrl": "/hubs/game"');
});
