import { expect, test } from '@playwright/test';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';

type Coordination = {
  backendUrl: string;
  roomCode: string;
  contentPackageVersionId: string;
  iosNickname: string;
  displayExpected: boolean;
  scriptedPlayers: string[];
};

test.describe('Display Mixed Client E2E (iOS + scripted players)', () => {
  test('attaches to the orchestrated room and observes its single start', async ({
    page,
  }) => {
    test.setTimeout(120_000);
    const coordinationDir = process.env.PARTYGAME_E2E_COORDINATION_DIR;
    if (!coordinationDir)
      throw new Error('PARTYGAME_E2E_COORDINATION_DIR is required.');

    const coordination = JSON.parse(
      await readFile(join(coordinationDir, 'coordination.json'), 'utf8'),
    ) as Coordination;
    expect(coordination.displayExpected).toBe(true);
    expect(process.env.PLAYWRIGHT_API_URL).toBe(coordination.backendUrl);

    await page.goto('/display');
    await page.getByLabel('Kod pokoju').fill(coordination.roomCode);
    await page.getByRole('button', { name: 'Połącz ekran' }).click();

    await expect(page.getByText(coordination.roomCode).first()).toBeVisible();
    for (const name of [
      ...coordination.scriptedPlayers,
      coordination.iosNickname,
    ]) {
      await expect(page.getByText(name).first()).toBeVisible({
        timeout: 30_000,
      });
    }

    await writeFile(join(coordinationDir, 'display-attached'), '');
    await expect(page.locator('.game-screen-container')).toBeVisible({
      timeout: 60_000,
    });
  });
});
