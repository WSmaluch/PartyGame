import fs from 'node:fs/promises';
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const [baseUrl, accessFile] = process.argv.slice(2);
if (!baseUrl || !accessFile) throw new Error('Usage: test-web-player-drawing-webkit.mjs <player-url> <access-file>');

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const requireFromDisplay = createRequire(path.join(repo, 'apps/display-web/package.json'));
const { webkit } = requireFromDisplay('@playwright/test');

let access;
for (let attempt = 0; attempt < 300; attempt += 1) {
  try {
    access = JSON.parse(await fs.readFile(accessFile, 'utf8'));
    break;
  } catch {
    await new Promise((resolve) => setTimeout(resolve, 100));
  }
}
if (!access) throw new Error('Timed out waiting for safe external-player access file.');

const browser = await webkit.launch({ headless: true });
try {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  const page = await context.newPage();
  const consoleEntries = [];
  const pageErrors = [];
  page.on('console', (message) => consoleEntries.push(`${message.type()}: ${message.text()}`));
  page.on('pageerror', (error) => pageErrors.push(error.message));
  await page.addInitScript((session) => localStorage.setItem('partygame.player.session', JSON.stringify(session)), access);
  // SignalR intentionally keeps a transport request active throughout the
  // game, so networkidle is not a meaningful navigation condition here.
  await page.goto(`${baseUrl}/play/?diagnostics=1`, { waitUntil: 'domcontentloaded' });
  // WebKit does not expose canvas role=img consistently in its accessibility
  // tree, even though the DOM carries the label. Mount and geometry are the
  // contract under test here, so select the rendered canvas element directly.
  const canvas = page.locator('canvas.drawing-canvas');
  try {
    await canvas.waitFor({ state: 'visible', timeout: 15_000 });
  } catch (error) {
    throw new Error(`WebKit did not mount Drawing: text=${JSON.stringify((await page.locator('body').innerText()).slice(0, 1_000))}; pageErrors=${pageErrors.join(' | ')}; console=${consoleEntries.join(' | ')}`, { cause: error });
  }
  await page.getByRole('button', { name: 'Wybierz kolor' }).click();
  await page.getByRole('button', { name: 'Czarny' }).waitFor({ state: 'visible' });
  await page.getByRole('button', { name: 'Wybierz narzędzie' }).click();
  await page.getByRole('button', { name: 'Gumka' }).waitFor({ state: 'visible' });
  await page.getByRole('button', { name: 'Wyślij rysunek' }).waitFor({ state: 'visible' });
  if (await page.getByText('Ładowanie gry…').count()) throw new Error('WebKit rendered permanent game-loading fallback instead of normal Drawing.');
  const box = await canvas.boundingBox();
  if (!box || box.width < 100 || box.height < 100) throw new Error(`Drawing canvas geometry is not actionable: ${JSON.stringify(box)}`);
  await page.mouse.move(box.x + box.width * 0.2, box.y + box.height * 0.2);
  await page.mouse.down();
  await page.mouse.move(box.x + box.width * 0.8, box.y + box.height * 0.8, { steps: 5 });
  await page.mouse.up();
  const submit = page.getByRole('button', { name: 'Wyślij rysunek' });
  if (await submit.isDisabled()) throw new Error('Drawing ink did not enable submit in WebKit.');
  await submit.click();
  await page.getByText('Rysunek wysłany').waitFor({ state: 'visible', timeout: 30_000 });
  if (pageErrors.length) throw new Error(`WebKit page error: ${pageErrors.join(' | ')}`);
  const routerEntry = consoleEntries.find((entry) => entry.includes('[PartyGame player router]') && entry.includes('actionable-private-state-matched'));
  if (!routerEntry) throw new Error(`Missing actionable Drawing router diagnostic: ${consoleEntries.join(' | ')}`);
  if (consoleEntries.some((entry) => entry.includes(access.reconnectToken))) throw new Error('Router diagnostics exposed a reconnect token.');
  console.log(`WebKit Web Player normal Drawing PASS: canvas=${Math.round(box.width)}x${Math.round(box.height)}, toolbar=visible, upload=confirmed`);
  await context.close();
} finally {
  await browser.close();
}
