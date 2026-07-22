import { expect, test } from '@playwright/test';

test('real category CRUD, persistence, reorder and concurrency', async ({ page, request, browser }) => {
  const api = process.env.ADMIN_E2E_API_URL!;
  const created = await request.post(`${api}/api/admin/content-packages`, { data: { namePl: 'E2E kategorie', nameEn: 'E2E categories' } });
  expect(created.ok()).toBeTruthy(); const pkg = await created.json();
  await page.goto(`/admin/content/packages/${pkg.id}/categories`);
  await expect(page.getByText(/nie ma jeszcze kategorii/)).toBeVisible();
  const add = async (key: string, pl: string, en: string) => { await page.getByLabel('Key').fill(key); await page.getByLabel('Nazwa PL').fill(pl); await page.getByLabel('Nazwa EN').fill(en); await page.getByRole('button', { name: 'Zapisz' }).click(); await expect(page.getByText(pl, { exact: true })).toBeVisible(); };
  await add('cat_a', 'Kategoria A', 'Category A'); await add('cat_b', 'Kategoria B', 'Category B');
  await page.getByRole('listitem').filter({ hasText: 'Kategoria A' }).getByRole('button', { name: 'Edytuj' }).click(); await page.getByLabel('Nazwa PL').fill('Kategoria A edytowana'); await page.getByRole('button', { name: 'Zapisz' }).click();
  const rowA = page.getByRole('listitem').filter({ hasText: 'Kategoria A edytowana' }); await rowA.getByRole('button', { name: 'Wyłącz' }).click(); await expect(rowA).toContainText('Wyłączona'); await rowA.getByRole('button', { name: 'Włącz' }).click();
  await Promise.all([page.waitForResponse(response => response.url().endsWith('/categories/reorder') && response.ok()), page.getByRole('listitem').filter({ hasText: 'Kategoria B' }).getByRole('button', { name: 'Przenieś wyżej' }).click()]); await page.reload(); await expect(page.getByRole('listitem').first()).toContainText('Kategoria B');

  let categoriesResponse = await request.get(`${api}/api/admin/content-packages/${pkg.id}/categories`); let categories = await categoriesResponse.json();
  const categoryA = categories.items.find((item: { key: string }) => item.key === 'cat_a'); const categoryB = categories.items.find((item: { key: string }) => item.key === 'cat_b');
  const helperQuestion = await request.post(`${api}/api/admin/content-packages/${pkg.id}/questions`, { data: { categoryId: categoryA.id, key: 'helper_a', type: 'PlayerSelection', textPl: 'Kto wybiera < zwykły tekst?', textEn: 'Who chooses?', minimumPlayers: 3 } }); expect(helperQuestion.ok()).toBeTruthy();
  await page.reload(); const rowWithQuestion = page.getByRole('listitem').filter({ hasText: 'Kategoria A edytowana' }); await expect(rowWithQuestion).toContainText('1 pytań'); await rowWithQuestion.getByRole('button', { name: 'Usuń' }).click(); await expect(page.getByRole('dialog')).toContainText('1 pytań'); await page.getByLabel('Kategoria docelowa').selectOption(categoryB.id); await page.getByRole('button', { name: 'Przenieś pytania' }).click(); await expect(page.getByText('Kategoria A edytowana', { exact: true })).toHaveCount(0);
  const movedQuestions = await (await request.get(`${api}/api/admin/content-packages/${pkg.id}/questions?categoryId=${categoryB.id}`)).json(); expect(movedQuestions.items.some((item: { key: string }) => item.key === 'helper_a')).toBeTruthy();

  await add('cat_c', 'Kategoria C', 'Category C'); categoriesResponse = await request.get(`${api}/api/admin/content-packages/${pkg.id}/categories`); categories = await categoriesResponse.json(); const categoryC = categories.items.find((item: { key: string }) => item.key === 'cat_c');
  const questionC = await request.post(`${api}/api/admin/content-packages/${pkg.id}/questions`, { data: { categoryId: categoryC.id, key: 'helper_c', type: 'TextAnswer', textPl: 'Tekst pomocniczy', textEn: 'Helper text', minimumPlayers: 3 } }); expect(questionC.ok()).toBeTruthy(); await page.reload(); const rowC = page.getByRole('listitem').filter({ hasText: 'Kategoria C' }); await rowC.getByRole('button', { name: 'Usuń' }).click(); await page.getByRole('button', { name: 'Usuń kategorię i pytania' }).click(); await expect(page.getByText('Kategoria C', { exact: true })).toHaveCount(0);

  const second = await browser.newContext(); const pageB = await second.newPage(); await pageB.goto(`/admin/content/packages/${pkg.id}/categories`);
  await page.getByRole('listitem').filter({ hasText: 'Kategoria B' }).getByRole('button', { name: 'Edytuj' }).click();
  await pageB.getByRole('listitem').filter({ hasText: 'Kategoria B' }).getByRole('button', { name: 'Edytuj' }).click(); await pageB.getByLabel('Nazwa PL').fill('Zmiana B'); await pageB.getByRole('button', { name: 'Zapisz' }).click();
  await page.getByLabel('Nazwa PL').fill('Zmiana A'); await page.getByRole('button', { name: 'Zapisz' }).click(); await expect(page.getByRole('alert')).toContainText('zmieniona w innej sesji'); await expect(page.getByLabel('Nazwa PL')).toHaveValue('Zmiana A'); await page.getByRole('button', { name: 'Odśwież dane' }).click(); await expect(page.getByText('Zmiana B', { exact: true })).toBeVisible(); await expect(page.getByLabel('Nazwa PL')).toHaveValue('Zmiana B');
  await second.close();
});
