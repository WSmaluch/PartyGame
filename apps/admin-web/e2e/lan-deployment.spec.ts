import { expect, test } from '@playwright/test';

test('boots the deployed Admin with a JSON runtime config', async ({
  page,
}) => {
  const pageErrors: string[] = [];
  page.on('pageerror', (error) => pageErrors.push(error.message));

  await page.goto('/admin/');
  await expect(
    page.getByRole('heading', { name: 'PartyGame Admin' }),
  ).toBeVisible();
  await expect(page.getByRole('alert')).toHaveCount(0);

  const config = await page.evaluate(async () => {
    const response = await fetch('/admin/config.json', { cache: 'no-store' });
    return {
      ok: response.ok,
      contentType: response.headers.get('content-type'),
      body: await response.text(),
    };
  });
  expect(config.ok).toBe(true);
  expect(config.contentType).toMatch(/^application\/json(?:;|$)/i);
  expect(() => JSON.parse(config.body)).not.toThrow();
  expect(config.body).not.toContain('<html');
  expect(config.body).not.toContain('localhost');
  expect(config.body).not.toContain('127.0.0.1');
  expect(pageErrors).toEqual([]);
});
