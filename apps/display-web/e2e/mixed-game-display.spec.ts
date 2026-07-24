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

const stages = [
  ['playerselection', 'collecting', '.collecting-selections'],
  ['playerselection', 'results', '.showing-results'],
  ['textanswer', 'collecting', '.collecting-text-answers'],
  ['textanswer', 'revealing', '.revealing-answers'],
  ['textanswer', 'voting', '.collecting-text-votes'],
  ['textanswer', 'results', '.showing-text-results'],
  ['photoanswer', 'collecting', '[data-testid="collecting-photo-answers"]'],
  ['photoanswer', 'revealing', '[data-testid="revealing-photo-answers"]'],
  ['photoanswer', 'voting', '[data-testid="collecting-photo-answer-votes"]'],
  ['photoanswer', 'results', '[data-testid="showing-photo-answer-results"]'],
  ['drawinganswer', 'collecting', '[data-testid="drawing-collecting"]'],
  ['drawinganswer', 'revealing', '[data-testid="revealing-drawing-answers"]'],
  [
    'drawinganswer',
    'voting',
    '[data-testid="collecting-drawing-answer-votes"]',
  ],
  [
    'drawinganswer',
    'results',
    '[data-testid="showing-drawing-answer-results"]',
  ],
] as const;

test.describe('Display Mixed Client E2E (iOS + scripted players)', () => {
  test('attaches and observes every dynamically planned question through completion', async ({
    page,
  }) => {
    test.setTimeout(300_000);
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
    ])
      await expect(page.getByText(name).first()).toBeVisible({
        timeout: 30_000,
      });
    await writeFile(join(coordinationDir, 'display-attached'), '');

    const observed = new Set<string>();
    const deadline = Date.now() + 240_000;
    while (
      Date.now() < deadline &&
      !(await page.locator('.game-completed').isVisible())
    ) {
      for (const [type, phase, selector] of stages) {
        const key = `${type}-${phase}`;
        if (!observed.has(key) && (await page.locator(selector).isVisible())) {
          observed.add(key);
          await writeFile(join(coordinationDir, `display-${key}`), '');
        }
      }
      await page.waitForTimeout(100);
    }

    await expect(page.locator('.game-completed')).toBeVisible({
      timeout: 1_000,
    });
    for (const name of [
      ...coordination.scriptedPlayers,
      coordination.iosNickname,
    ])
      await expect(page.locator('.game-completed')).toContainText(name);
    for (const type of [
      'playerselection',
      'textanswer',
      'photoanswer',
      'drawinganswer',
    ]) {
      expect(observed.has(`${type}-collecting`)).toBe(true);
      expect(observed.has(`${type}-results`)).toBe(true);
    }
    for (const type of ['textanswer', 'photoanswer', 'drawinganswer'])
      expect(observed.has(`${type}-voting`)).toBe(true);
    await writeFile(join(coordinationDir, 'display-completed'), '');
  });
});
