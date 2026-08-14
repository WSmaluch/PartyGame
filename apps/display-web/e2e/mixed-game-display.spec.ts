import { expect, test } from '@playwright/test';
import { readFile, rename, writeFile } from 'node:fs/promises';
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
  ['finalround', 'selfies', '[data-testid="final-round-selfies-collecting"]'],
  ['finalround', 'edits', '[data-testid="final-round-edits-collecting"]'],
  ['finalround', 'presentation', '[data-testid="final-round-presentation"]'],
  ['finalround', 'voting', '[data-testid="final-round-voting"]'],
  ['finalround', 'results', '[data-testid="final-round-results"]'],
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
    await writeMarker(coordinationDir, 'display-attached');
    console.log('display-attached marker written');
    await recordDisplayObservation(
      page,
      coordinationDir,
      'snapshot-initial-attach',
      1,
    );

    const observed = new Set<string>();
    let displayReconnected = false;
    const deadline = Date.now() + 360_000;
    while (
      Date.now() < deadline &&
      !(await page.locator('.game-completed').isVisible())
    ) {
      for (const [type, phase, selector] of stages) {
        const key = `${type}-${phase}`;
        if (!observed.has(key) && (await page.locator(selector).isVisible())) {
          observed.add(key);
          await writeFile(join(coordinationDir, `display-${key}`), '');
          if (
            !displayReconnected &&
            (await fileExists(join(coordinationDir, 'ios-recovered-state')))
          ) {
            const before = await displayStateVersion(page);
            await recordDisplayObservation(
              page,
              coordinationDir,
              'snapshot-before-reload',
              2,
            );
            await writeDisplayMarker(coordinationDir, 'display-before-reload', {
              event: 'display-before-reload', stateVersion: before, gameStage: 'Active', rankingCount: 0,
            });
            await writeFile(
              join(coordinationDir, 'display-reconnect-before.json'),
              JSON.stringify({
                client: 'display',
                stateVersion: before,
                timestampUtc: new Date().toISOString(),
              }),
            );
            await writeFile(
              join(coordinationDir, 'display-reconnect-requested'),
              '',
            );
            await page.reload();
            await expect(
              page.getByTestId('display-state-version'),
            ).toHaveAttribute('data-state-version', /^\d+$/, {
              timeout: 30_000,
            });
            const recovered = await displayStateVersion(page);
            expect(recovered).toBeGreaterThanOrEqual(before);
            await recordDisplayObservation(
              page,
              coordinationDir,
              'snapshot-after-reconnect',
              3,
            );
            await writeDisplayMarker(coordinationDir, 'display-after-reconnect', {
              event: 'display-after-reconnect', stateVersion: recovered, gameStage: 'Active', rankingCount: 0,
            });
            await writeFile(
              join(coordinationDir, 'display-reconnect-after.json'),
              JSON.stringify({
                client: 'display',
                stateVersion: recovered,
                timestampUtc: new Date().toISOString(),
              }),
            );
            await writeFile(join(coordinationDir, 'display-reconnected'), '');
            displayReconnected = true;
          }
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
    for (const type of ['playerselection', 'textanswer', 'photoanswer', 'drawinganswer']) {
      expect(observed.has(`${type}-collecting`)).toBe(true);
    }
    for (const type of ['textanswer', 'photoanswer', 'drawinganswer'])
      expect(observed.has(`${type}-voting`)).toBe(true);
    for (const phase of ['selfies', 'edits', 'presentation', 'voting', 'results'])
      expect(observed.has(`finalround-${phase}`)).toBe(true);
    const rankingCount = await page.locator('.game-completed .ranking-entry').count();
    expect(rankingCount).toBe(coordination.scriptedPlayers.length + 1);
    const terminalStateVersion = await displayStateVersion(page);
    await writeDisplayMarker(coordinationDir, 'display-ranking-observed', {
      event: 'display-ranking-observed',
      stateVersion: terminalStateVersion,
      gameStage: 'Completed',
      rankingCount,
    });
    await writeDisplayMarker(coordinationDir, 'display-completed', {
      event: 'display-completed',
      stateVersion: terminalStateVersion,
      gameStage: 'Completed',
      rankingCount,
    });
    await recordDisplayObservation(
      page,
      coordinationDir,
      'snapshot-completed',
      4,
      'Completed',
    );
    expect(displayReconnected).toBe(true);
  });
});

async function displayStateVersion(
  page: import('@playwright/test').Page,
): Promise<number> {
  const value = await page
    .getByTestId('display-state-version')
    .getAttribute('data-state-version');
  if (!value || !/^\d+$/.test(value))
    throw new Error(
      `Invalid accepted Display stateVersion: ${value ?? '<missing>'}`,
    );
  return Number(value);
}

async function recordDisplayObservation(
  page: import('@playwright/test').Page,
  directory: string,
  event: string,
  sequence: number,
  phase = 'rendered',
): Promise<void> {
  const version = await displayStateVersion(page);
  const value = {
    client: 'display',
    event,
    stateVersion: version,
    phase,
    questionId: '',
    timestampUtc: new Date().toISOString(),
  };
  const path = join(
    directory,
    `display-observation-${String(sequence).padStart(6, '0')}.json`,
  );
  const temporaryPath = `${path}.tmp`;
  await writeFile(temporaryPath, JSON.stringify(value));
  await rename(temporaryPath, path);
}

async function fileExists(path: string): Promise<boolean> {
  try {
    await readFile(path);
    return true;
  } catch {
    return false;
  }
}

async function writeMarker(directory: string, name: string): Promise<void> {
  await writeDisplayMarker(directory, name, { event: name });
}

async function writeDisplayMarker(
  directory: string,
  name: string,
  value: Record<string, unknown>,
): Promise<void> {
  const path = join(directory, name);
  try {
    const existing = JSON.parse(await readFile(path, 'utf8')) as Record<string, unknown>;
    if (JSON.stringify(existing) !== JSON.stringify(value))
      throw new Error(`Conflicting Display marker: ${name}`);
    return;
  } catch (error) {
    if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error;
  }
  const temporaryPath = `${path}.tmp`;
  await writeFile(temporaryPath, JSON.stringify(value));
  await rename(temporaryPath, path);
  JSON.parse(await readFile(path, 'utf8'));
}
