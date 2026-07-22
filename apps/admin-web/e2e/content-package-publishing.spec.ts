import { expect, test } from '@playwright/test';

test('real package lifecycle: UI publishing, version binding, archive and conflict', async ({ page, request, browser }) => {
  const api = process.env.ADMIN_E2E_API_URL!;
  const created = await request.post(`${api}/api/admin/content-packages`, { data: { namePl: 'E2E publikacja', nameEn: 'E2E publishing' } });
  expect(created.ok()).toBeTruthy();
  const v1Draft = await created.json();

  await page.goto(`/admin/content/packages/${v1Draft.id}`);
  await page.getByLabel('Nazwa PL').fill('E2E metadane przez UI');
  await page.getByRole('button', { name: 'Zapisz' }).click();
  await expect(page.getByRole('heading', { name: 'E2E metadane przez UI' })).toBeVisible();
  await page.getByRole('button', { name: 'Publikuj' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publikuj' }).click();
  await expect(page.getByRole('dialog')).toContainText('musi posiadać co najmniej jedną aktywną kategorię');

  const afterMetadata = await (await request.get(`${api}/api/admin/content-packages/${v1Draft.id}`)).json();
  const categoryResponse = await request.post(`${api}/api/admin/content-packages/${v1Draft.id}/categories`, { data: { key: 'publish_cat', namePl: 'Publikacja', nameEn: 'Publishing', packageConcurrencyToken: afterMetadata.concurrencyToken } });
  expect(categoryResponse.ok()).toBeTruthy();
  const categoryBody = await categoryResponse.json();
  const questionResponse = await request.post(`${api}/api/admin/content-packages/${v1Draft.id}/questions`, { data: { categoryId: categoryBody.category.id, key: 'publish_question', type: 'TextAnswer', textPl: 'Publikuj', textEn: 'Publish', minimumPlayers: 3 } });
  expect(questionResponse.ok()).toBeTruthy();

  await page.reload();
  await page.getByRole('button', { name: 'Publikuj' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publikuj' }).click();
  await expect(page.getByText(/tylko do odczytu/)).toBeVisible();
  await expect(page.getByLabel('Nazwa PL')).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'Kategorie' })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Pytania' })).toBeVisible();

  const v1 = await (await request.get(`${api}/api/admin/content-packages/${v1Draft.id}`)).json();
  const oldRoomResponse = await request.post(`${api}/api/rooms`, { data: { nickname: 'HistoryHost', contentPackageVersionId: v1.id } });
  expect(oldRoomResponse.ok()).toBeTruthy();
  const oldRoom = await oldRoomResponse.json();
  expect(oldRoom.snapshot.contentPackageVersionId).toBe(v1.id);

  await page.getByRole('button', { name: 'Utwórz Draft' }).click();
  await expect(page.getByText(/Draft · v2/)).toBeVisible();
  const v2Id = page.url().split('/').pop()!;
  const v2Detail = await (await request.get(`${api}/api/admin/content-packages/${v2Id}`)).json();
  expect(v2Detail.categoryCount).toBe(1);
  expect(v2Detail.questionCount).toBe(1);
  await page.goto(`/admin/content/packages/${v1.id}`);
  await page.getByRole('button', { name: 'Utwórz Draft' }).click();
  await expect(page.getByRole('alert')).toContainText('zmieniony w innej sesji');
  await page.goto(`/admin/content/packages/${v2Id}`);

  await page.getByRole('button', { name: 'Publikuj' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Publikuj' }).click();
  await expect(page.getByText(/Published · v2/)).toBeVisible();
  const v2Room = await request.post(`${api}/api/rooms`, { data: { nickname: 'V2Host', contentPackageVersionId: v2Id } });
  expect(v2Room.ok()).toBeTruthy();
  expect((await v2Room.json()).snapshot.contentPackageVersionId).toBe(v2Id);

  await page.goto(`/admin/content/packages/${v1.id}`);
  await page.getByRole('button', { name: 'Archiwizuj' }).click();
  await page.getByRole('dialog').getByRole('button', { name: 'Archiwizuj' }).click();
  await expect(page.getByText(/Archived · v1/)).toBeVisible();
  const rejectedRoom = await request.post(`${api}/api/rooms`, { data: { nickname: 'ArchivedHost', contentPackageVersionId: v1.id } });
  expect(rejectedRoom.status()).toBe(400);
  const oldRoomSnapshot = await request.get(`${api}/api/rooms/${oldRoom.roomCode}`);
  expect((await oldRoomSnapshot.json()).contentPackageVersionId).toBe(v1.id);

  await page.getByRole('button', { name: 'Utwórz Draft' }).click();
  await expect(page.getByText(/Draft · v3/)).toBeVisible();
  const v3Id = page.url().split('/').pop()!;
  const secondContext = await browser.newContext({ baseURL: process.env.ADMIN_E2E_BASE_URL });
  const secondPage = await secondContext.newPage();
  await secondPage.goto(`/admin/content/packages/${v3Id}`);
  await secondPage.getByLabel('Nazwa PL').fill('Zmiana z drugiej sesji');
  await secondPage.getByRole('button', { name: 'Zapisz' }).click();
  await page.getByLabel('Nazwa PL').fill('Niezapisana zmiana pierwszej sesji');
  await page.getByRole('button', { name: 'Zapisz' }).click();
  await expect(page.getByRole('alert')).toContainText('zmieniony w innej sesji');
  await expect(page.getByLabel('Nazwa PL')).toHaveValue('Niezapisana zmiana pierwszej sesji');
  await page.getByRole('button', { name: 'Odśwież dane' }).click();
  await expect(page.getByLabel('Nazwa PL')).toHaveValue('Zmiana z drugiej sesji');
  await secondContext.close();
});
