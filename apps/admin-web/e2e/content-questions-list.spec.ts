import { expect, test } from '@playwright/test';

test('real questions list filters, paginates and mutates content', async ({
  page,
  request,
  browser,
}) => {
  const api = process.env.ADMIN_E2E_API_URL!;
  const created = await request.post(`${api}/api/admin/content-packages`, {
    data: { namePl: 'E2E pytania', nameEn: 'E2E questions' },
  });
  expect(created.ok()).toBeTruthy();
  const pkg = await created.json();
  let packageToken = pkg.concurrencyToken;
  const createCategory = async (key: string, namePl: string) => {
    const response = await request.post(
      `${api}/api/admin/content-packages/${pkg.id}/categories`,
      {
        data: {
          key,
          namePl,
          nameEn: namePl,
          packageConcurrencyToken: packageToken,
        },
      },
    );
    expect(response.ok()).toBeTruthy();
    const body = await response.json();
    packageToken = body.packageConcurrencyToken;
    return body.category;
  };
  const firstCategory = await createCategory('first', 'Pierwsza');
  const secondCategory = await createCategory('second', 'Druga');
  const types = [
    'PlayerSelection',
    'TextAnswer',
    'PhotoAnswer',
    'DrawingAnswer',
  ];
  for (let index = 0; index < 12; index++) {
    const response = await request.post(
      `${api}/api/admin/content-packages/${pkg.id}/questions`,
      {
        data: {
          categoryId: index < 8 ? firstCategory.id : secondCategory.id,
          key: `question_${index}`,
          type: types[index % types.length],
          textPl: `Pytanie ${index}`,
          textEn: index === 11 ? '' : `Question ${index}`,
          minimumPlayers: 3,
        },
      },
    );
    if (index === 11) expect(response.status()).toBe(400);
    else expect(response.ok()).toBeTruthy();
  }
  const clearFilters = async () => {
    await page.getByRole('button', { name: 'Wyczyść filtry' }).click();
    await page.waitForURL(
      (url) =>
        ![
          'search',
          'categoryId',
          'questionType',
          'isEnabled',
          'missingTranslation',
          'validationErrors',
        ].some((name) => url.searchParams.has(name)),
    );
    await expect(page.getByLabel('Wyszukaj')).toHaveValue('');
    await expect(page.getByRole('combobox', { name: 'Kategoria' })).toHaveValue(
      '',
    );
  };
  await page.goto(`/admin/content/packages/${pkg.id}/questions?pageSize=10`);
  await expect(page.getByText('Pytanie 0')).toBeVisible();
  await expect(page.getByText(/Strona 1 z 2/)).toBeVisible();
  await page.getByLabel('Wyszukaj').fill('Pytanie 1');
  await page.getByRole('button', { name: 'Szukaj' }).click();
  await expect(page.getByText('Pytanie 1', { exact: true })).toBeVisible();
  await clearFilters();
  await expect(page.getByText('Pytanie 0')).toBeVisible();
  await page
    .getByRole('combobox', { name: 'Kategoria' })
    .selectOption(firstCategory.id);
  await page.waitForURL(
    (url) => url.searchParams.get('categoryId') === firstCategory.id,
  );
  await page.getByRole('combobox', { name: 'Typ' }).selectOption('TextAnswer');
  await page.waitForURL(
    (url) => url.searchParams.get('questionType') === 'TextAnswer',
  );
  await expect(page.getByText('Pytanie 1', { exact: true })).toBeVisible();
  await clearFilters();
  await page.getByLabel('Brak tłumaczenia').click();
  await page.waitForURL(
    (url) => url.searchParams.get('missingTranslation') === 'true',
  );
  await expect(
    page.getByText('Brak pytań spełniających wybrane kryteria.'),
  ).toBeVisible();
  await clearFilters();
  await page.getByLabel('Błędy walidacji').click();
  await page.waitForURL(
    (url) => url.searchParams.get('validationErrors') === 'true',
  );
  await expect(
    page.getByText('Brak pytań spełniających wybrane kryteria.'),
  ).toBeVisible();
  await clearFilters();
  await page.getByRole('button', { name: 'Następna' }).click();
  await expect(page.getByText(/Strona 2 z 2/)).toBeVisible();
  await page.getByLabel('Wyników na stronę').selectOption('25');
  await expect(page.getByText(/Strona 1 z 1/)).toBeVisible();
  const row = page.getByRole('row').filter({ hasText: 'question_0' });
  await row.getByRole('button', { name: 'Wyłącz' }).click();
  await expect(row).toContainText('Wyłączone');
  await row.getByRole('button', { name: 'Włącz' }).click();
  await row.getByRole('button', { name: 'Duplikuj' }).click();
  await expect(page.getByText('Pytanie zostało zduplikowane.')).toBeVisible();
  await page.getByLabel('Wyszukaj').fill('question_0_copy');
  await page.getByRole('button', { name: 'Szukaj' }).click();
  const duplicate = page
    .getByRole('row')
    .filter({ hasText: 'question_0_copy' });
  await expect(duplicate).toBeVisible();
  await duplicate.getByRole('button', { name: 'Usuń' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Usuń' }).click();
  await expect(duplicate).toHaveCount(0);
  await clearFilters();
  await page
    .getByRole('combobox', { name: 'Kategoria' })
    .selectOption(firstCategory.id);
  await page.waitForURL(
    (url) => url.searchParams.get('categoryId') === firstCategory.id,
  );
  const rows = page.getByRole('row').filter({ hasText: 'Pytanie' });
  await expect(rows).toHaveCount(8);
  await rows.nth(1).getByRole('button', { name: 'Przenieś wyżej' }).click();
  await expect(rows.nth(0)).toContainText('question_1');
  const second = await browser.newContext();
  const pageB = await second.newPage();
  await pageB.goto(
    `/admin/content/packages/${pkg.id}/questions?categoryId=${firstCategory.id}`,
  );
  const rowB = pageB.getByRole('row').filter({ hasText: 'question_0' });
  await rowB.getByRole('button', { name: 'Wyłącz' }).click();
  await page.getByRole('button', { name: 'Włącz' }).click();
  await expect(page.getByRole('alert')).toContainText(
    'zmienione w innej sesji',
  );
  await page.getByRole('button', { name: 'Odśwież listę' }).click();
  await second.close();
  const freshPackage = await (
    await request.get(`${api}/api/admin/content-packages/${pkg.id}`)
  ).json();
  const publish = await request.post(
    `${api}/api/admin/content-packages/${pkg.id}/publish`,
    { data: { concurrencyToken: freshPackage.concurrencyToken } },
  );
  expect(publish.ok()).toBeTruthy();
  await page.reload();
  await expect(page.getByText(/tylko do odczytu/)).toBeVisible();
  await expect(page.getByRole('button', { name: 'Szukaj' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Duplikuj' })).toHaveCount(0);
});
