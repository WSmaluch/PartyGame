import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const [playerUrl, apiUrl] = process.argv.slice(2);
if (!playerUrl || !apiUrl)
  throw new Error('Usage: test-web-player-final-selfie-webkit.mjs <player-url> <api-url>');

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
// Playwright is provisioned with display-web for the existing browser E2E
// suite; keep this harness aligned with that local, offline dependency.
const requireFromDisplay = createRequire(path.join(repo, 'apps/display-web/package.json'));
const { webkit } = requireFromDisplay('@playwright/test');
const requireFromPlayer = createRequire(path.join(repo, 'apps/player-web/package.json'));
const { HubConnectionBuilder } = requireFromPlayer('@microsoft/signalr');
const startedAt = Date.now();

const browser = await webkit.launch({ headless: true });
try {
  const happy = await runScenario('happy', false);
  const retry = await runScenario('retry', true);
  console.log('RC12 FINAL SELFIE WEB E2E PASS');
  console.log(JSON.stringify({
    browser: 'WebKit', contextsPerScenario: 3,
    happy: summary(happy), retry: summary(retry),
    durationMs: Date.now() - startedAt,
  }));
} finally {
  await browser.close();
}

async function runScenario(name, injectFailure) {
  const timeline = [];
  const contexts = [];
  const players = [];
  const httpStatuses = [];
  let display;
  let intercepted = false;
  try {
    for (const nickname of ['Aster', 'Birch', 'Cedar']) {
      const context = await browser.newContext({
        viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true,
      });
      const page = await context.newPage();
      const entry = { nickname, context, page, events: [], pageErrors: [] };
      page.on('console', (message) => {
        const text = message.text();
        // Keep the persisted diagnostic transcript deliberately credentials-free.
        if (text.includes('reconnectToken')) throw new Error(`${name}/${nickname}: browser diagnostics leaked a reconnect token.`);
        if (text.includes('[PartyGame final-selfie]') || text.includes('[PartyGame photo-preparation]')) entry.events.push(text);
      });
      page.on('pageerror', (error) => entry.pageErrors.push(error.message));
      page.on('response', (response) => {
        if (response.url().includes('/final-round/selfies')) httpStatuses.push({ nickname, status: response.status() });
      });
      contexts.push(context); players.push(entry);
    }

    await hostRoom(players[0]);
    const roomCode = await readRoomCode(players[0].page);
    // A room is deliberately startable only with a Display attached. This is
    // setup transport, not a player or a game-state mutation; the three actual
    // participants below remain independent production /play/ clients.
    display = new HubConnectionBuilder().withUrl(`${apiUrl}/hubs/game`).build();
    await display.start();
    await display.invoke('AttachDisplay', roomCode);
    for (const player of players.slice(1)) await joinRoom(player, roomCode);
    for (const player of players) {
      player.session = await player.page.evaluate(() => JSON.parse(localStorage.getItem('partygame.player.session') ?? 'null'));
      if (!player.session?.playerId) throw new Error(`${name}/${player.nickname}: production player session was not created.`);
    }
    const identities = new Set(players.map((player) => player.session.playerId));
    if (identities.size !== 3) throw new Error(`${name}: browser contexts did not establish three unique player identities.`);
    timeline.push(`${name}: three isolated /play/ identities established`);

    for (let index = 0; index < players.length; index += 1)
      await uploadProfileAndReady(players[index], index);
    await driveNormalGame(players, roomCode, timeline);
    await waitForStage(roomCode, 'CollectingFinalSelfies', 45_000);
    await Promise.all(players.map(async (player) => player.page.locator('#final-selfie').waitFor({ state: 'attached', timeout: 15_000 })));
    timeline.push(`${name}: FinalSelfie mounted in all three WebKit players`);

    if (injectFailure) {
      await players[1].page.route('**/api/rooms/*/final-round/selfies', async (route) => {
        if (!intercepted) {
          intercepted = true;
          timeline.push(`${name}/Birch: controlled HTTP 503 injected`);
          await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ title: 'controlled final selfie failure' }) });
          return;
        }
        await route.continue();
      });
    }

    await submitSelfie(players[0], 0, timeline);
    await assertSelfieProgress(roomCode, 1, 'CollectingFinalSelfies', `${name} after selfie 1`);

    if (injectFailure) {
      await selectSelfie(players[1], 1, timeline);
      await players[1].page.getByRole('button', { name: 'Wyślij zdjęcie' }).click();
      await players[1].page.getByText('Nie udało się wysłać zdjęcia.').waitFor({ state: 'visible', timeout: 15_000 });
      await players[1].page.getByRole('button', { name: 'Spróbuj ponownie' }).waitFor({ state: 'visible' });
      if (await players[1].page.locator('img.media-preview').count() !== 1) throw new Error(`${name}/Birch: failed upload removed its selfie preview.`);
      await assertSelfieProgress(roomCode, 1, 'CollectingFinalSelfies', `${name} after controlled failure`);
      await players[1].page.unroute('**/api/rooms/*/final-round/selfies');
      timeline.push(`${name}/Birch: failure UI with retained preview observed; retry clicked`);
      await players[1].page.getByRole('button', { name: 'Spróbuj ponownie' }).click();
      await awaitSubmitted(players[1], timeline);
    } else {
      await submitSelfie(players[1], 1, timeline);
    }
    await assertSelfieProgress(roomCode, 2, 'CollectingFinalSelfies', `${name} after selfie 2`);
    // The final acknowledgement legitimately publishes the next stage in the
    // same response. Unlike players one and two, player three can therefore
    // move straight to Final Edit before a submitted waiting card paints.
    await submitSelfie(players[2], 2, timeline, true);
    const finalEdit = await waitForStage(roomCode, 'CollectingFinalEdits', 15_000);
    const selfieCount = finalEdit.game.finalRound.submittedSelfies;
    if (selfieCount !== 3) throw new Error(`${name}: Final Edit began with ${selfieCount}, not 3, persisted selfies.`);

    const assignments = await completeFinalEdits(players, roomCode, timeline);
    const audit = await submissionAudit(roomCode, players[0].session);
    const selfieAudit = audit.entries.filter((entry) => entry.actionType === 'FinalSelfie');
    const acceptedSelfies = selfieAudit.filter((entry) => entry.result === 'Accepted');
    const finalEditAudit = audit.entries.filter((entry) => entry.actionType === 'FinalEdit' && entry.result === 'Accepted');
    if (acceptedSelfies.length !== 3 || selfieAudit.length !== 3)
      throw new Error(`${name}: expected exactly three accepted FinalSelfie audit records, got accepted=${acceptedSelfies.length}, total=${selfieAudit.length}.`);
    if (finalEditAudit.length !== 6) throw new Error(`${name}: expected six accepted FinalEdit audit records, got ${finalEditAudit.length}.`);
    if (new Set(acceptedSelfies.map((entry) => entry.playerId)).size !== 3) throw new Error(`${name}: selfie audit does not have one accepted submission per player.`);
    if (players.some((player) => player.pageErrors.length)) throw new Error(`${name}: WebKit page errors: ${players.flatMap((player) => player.pageErrors.map((error) => `${player.nickname}:${error}`)).join(' | ')}`);
    if (injectFailure && !intercepted) throw new Error(`${name}: controlled upload interception did not occur.`);
    const status503 = httpStatuses.some((entry) => entry.nickname === 'Birch' && entry.status === 503);
    if (injectFailure && !status503) throw new Error(`${name}: browser did not observe the injected HTTP 503.`);

    return { name, identities: identities.size, acceptedSelfies: acceptedSelfies.length, selfieAudit: selfieAudit.length,
      failureStatus: injectFailure ? 503 : null, assignments: assignments.length, durationMs: Date.now() - startedAt,
      timeline, orphanMedia: 'none (the intercepted request never reached the server)', };
  } finally {
    if (display) await display.stop();
    await Promise.all(contexts.map((context) => context.close()));
  }
}

async function hostRoom(player) {
  const { page } = player;
  await page.goto(`${playerUrl}/play/`, { waitUntil: 'domcontentloaded' });
  await page.locator('button.entry-action').click();
  await page.locator('#hostNickname').fill(player.nickname);
  const settings = page.locator('input[type=number]');
  await settings.nth(0).fill('1');
  await settings.nth(1).fill('4');
  await settings.nth(2).fill('5');
  await settings.nth(5).fill('10');
  await settings.nth(6).fill('30');
  await settings.nth(7).fill('3');
  await settings.nth(8).fill('2');
  await page.locator('form button[type=submit]').click();
  await page.locator('.lobby-card').waitFor({ state: 'visible', timeout: 15_000 });
}

async function joinRoom(player, roomCode) {
  const { page } = player;
  await page.goto(`${playerUrl}/play/?room=${roomCode}`, { waitUntil: 'domcontentloaded' });
  await page.locator('#nickname').fill(player.nickname);
  await page.locator('form button[type=submit]').click();
  await page.locator('.lobby-card').waitFor({ state: 'visible', timeout: 15_000 });
}

async function uploadProfileAndReady(player, index) {
  const image = await makeImage(player.page, index);
  await player.page.locator('#profilePhoto').setInputFiles(image);
  await player.page.locator('.profile button').click();
  const ready = player.page.locator('.lobby-card > button').last();
  await ready.waitFor({ state: 'visible', timeout: 15_000 });
  await ready.click();
}

async function driveNormalGame(players, roomCode, timeline) {
  for (let question = 1; question <= 4; question += 1) {
    await waitForStage(roomCode, 'CollectingPlayerSelections', 40_000);
    await Promise.all(players.map(async (player) => {
      await player.page.getByRole('button', { name: player.nickname, exact: true }).click();
    }));
    timeline.push(`normal question ${question}: three production player selections submitted on first tap`);
    if (question < 4) await waitForStageChange(roomCode, 'CollectingPlayerSelections', 20_000);
  }
}

async function selectSelfie(player, index, timeline) {
  const image = await makeImage(player.page, index + 10);
  timeline.push(`${player.nickname}: image selected (640x480 image/png)`);
  await player.page.locator('#final-selfie').setInputFiles(image);
  await player.page.locator('img.media-preview').waitFor({ state: 'visible', timeout: 15_000 });
  await player.page.getByRole('button', { name: 'Wyślij zdjęcie' }).waitFor({ state: 'visible' });
  const preparation = await eventually(() => player.events.find((event) => event.includes('[PartyGame photo-preparation]')), 5_000);
  if (!preparation) throw new Error(`${player.nickname}: final selfie image preparation diagnostic was not observed.`);
  timeline.push(`${player.nickname}: preview and preparation completed`);
}

async function submitSelfie(player, index, timeline, finalAcknowledgementMayAdvance = false) {
  await selectSelfie(player, index, timeline);
  await player.page.getByRole('button', { name: 'Wyślij zdjęcie' }).click();
  await awaitSubmitted(player, timeline, finalAcknowledgementMayAdvance);
}

async function awaitSubmitted(player, timeline, finalAcknowledgementMayAdvance = false) {
  const acknowledged = await eventually(() => player.events.find((event) => event.includes('upload-acknowledged')), 5_000);
  if (!acknowledged) throw new Error(`${player.nickname}: final selfie private acknowledgement was not observed.`);
  if (finalAcknowledgementMayAdvance) {
    timeline.push(`${player.nickname}: upload acknowledged; authoritative next-stage transition allowed before submitted card paints`);
    return;
  }
  await player.page.getByText('Zdjęcie wysłane').waitFor({ state: 'visible', timeout: 20_000 });
  timeline.push(`${player.nickname}: upload acknowledged and submitted UI observed`);
}

async function completeFinalEdits(players, roomCode, timeline) {
  const recorded = [];
  for (let pass = 1; pass <= 2; pass += 1) {
    const room = await waitForStage(roomCode, 'CollectingFinalEdits', 15_000);
    const final = room.game.finalRound;
    if (final.currentPass !== pass || final.editAssignments?.length !== 3)
      throw new Error(`Final Edit pass ${pass}: expected three server assignments.`);
    for (const assignment of final.editAssignments) {
      const artifact = final.artifacts.find((item) => item.artifactId === assignment.artifactId);
      if (!artifact || artifact.subjectPlayerId === assignment.editorPlayerId)
        throw new Error(`Final Edit pass ${pass}: server assigned an owner to edit their own artifact.`);
      const key = `${pass}:${assignment.artifactId}:${assignment.editorPlayerId}`;
      if (recorded.some((item) => item.key === key)) throw new Error(`Final Edit duplicate assignment ${key}.`);
      recorded.push({ key, pass, ...assignment, ownerId: artifact.subjectPlayerId });
    }
    await Promise.all(players.map((player) => drawAndSubmit(player)));
    timeline.push(`Final Edit pass ${pass}: three production drawing submissions completed`);
    if (pass === 1) await eventuallyRoom(roomCode, (next) => next.game?.finalRound?.currentPass === 2 && next.game?.stage === 'CollectingFinalEdits', 15_000);
    else await waitForStage(roomCode, 'ShowingFinalPresentation', 20_000);
  }
  if (recorded.length !== 6 || new Set(recorded.map((item) => `${item.artifactId}:${item.editorPlayerId}`)).size !== 6)
    throw new Error(`Final Edit topology is not six unique owner/editor assignments.`);
  return recorded;
}

async function drawAndSubmit(player) {
  // StageTransition keeps the outgoing editor briefly mounted for its exit
  // animation. The most recently mounted canvas is the current pass.
  const canvas = player.page.locator('canvas.drawing-canvas').last();
  await canvas.waitFor({ state: 'visible', timeout: 15_000 });
  // RC14 intentionally permits vertical page scrolling outside the drawing
  // surface. Ensure a WebKit mouse gesture targets the actionable canvas,
  // rather than an off-viewport coordinate in the long Final Edit page.
  await canvas.scrollIntoViewIfNeeded();
  const box = await canvas.boundingBox();
  if (!box || box.width < 100 || box.height < 100) throw new Error(`${player.nickname}: Final Edit canvas is not actionable.`);
  await player.page.mouse.move(box.x + box.width * 0.2, box.y + box.height * 0.2);
  await player.page.mouse.down();
  await player.page.mouse.move(box.x + box.width * 0.8, box.y + box.height * 0.8, { steps: 4 });
  await player.page.mouse.up();
  const submit = player.page.getByRole('button', { name: 'Wyślij rysunek' });
  if (await submit.isDisabled()) throw new Error(`${player.nickname}: Final Edit ink did not enable submission.`);
  await submit.click();
}

async function assertSelfieProgress(roomCode, expected, stage, label) {
  const room = await eventuallyRoom(roomCode, (candidate) => candidate.game?.finalRound?.submittedSelfies === expected, 10_000);
  if (room.game.stage !== stage) throw new Error(`${label}: premature transition to ${room.game.stage}.`);
  if (room.game.finalRound.requiredSelfies !== 3) throw new Error(`${label}: expected exactly three required selfies.`);
}

async function submissionAudit(roomCode, session) {
  const url = new URL(`${apiUrl}/api/rooms/${encodeURIComponent(roomCode)}/submission-audit`);
  url.searchParams.set('playerId', session.playerId);
  url.searchParams.set('reconnectToken', session.reconnectToken);
  const response = await fetch(url);
  if (!response.ok) throw new Error(`Could not read safe submission audit: HTTP ${response.status}.`);
  return response.json();
}

async function room(roomCode) {
  const response = await fetch(`${apiUrl}/api/rooms/${encodeURIComponent(roomCode)}`);
  if (!response.ok) throw new Error(`Could not read room state: HTTP ${response.status}.`);
  return response.json();
}

async function waitForStage(roomCode, stage, timeout) {
  return eventuallyRoom(roomCode, (candidate) => candidate.game?.stage === stage, timeout);
}

async function waitForStageChange(roomCode, stage, timeout) {
  return eventuallyRoom(roomCode, (candidate) => candidate.game?.stage !== stage, timeout);
}

async function eventuallyRoom(roomCode, condition, timeout) {
  const deadline = Date.now() + timeout;
  let latest;
  while (Date.now() < deadline) {
    latest = await room(roomCode);
    if (condition(latest)) return latest;
    await delay(100);
  }
  const players = latest?.players?.map((player) => `${player.nickname}:ready=${player.isReady}:photo=${player.hasProfilePhoto}`).join(', ') ?? 'none';
  throw new Error(`Timed out waiting for authoritative room condition; phase=${latest?.phase ?? 'none'}, stage=${latest?.game?.stage ?? 'none'}, players=${players}.`);
}

async function eventually(read, timeout) {
  const deadline = Date.now() + timeout;
  while (Date.now() < deadline) {
    const value = read();
    if (value) return value;
    await delay(50);
  }
  return undefined;
}

async function makeImage(page, variant) {
  const dataUrl = await page.evaluate((shade) => {
    const canvas = document.createElement('canvas');
    canvas.width = 640; canvas.height = 480;
    const context = canvas.getContext('2d');
    if (!context) throw new Error('Canvas unavailable while creating deterministic test image.');
    context.fillStyle = `hsl(${shade * 41 % 360} 65% 55%)`;
    context.fillRect(0, 0, canvas.width, canvas.height);
    context.fillStyle = '#fff'; context.fillRect(80, 80, 480, 320);
    return canvas.toDataURL('image/png');
  }, variant);
  return { name: `final-selfie-${variant}.png`, mimeType: 'image/png', buffer: Buffer.from(dataUrl.split(',')[1], 'base64') };
}

async function readRoomCode(page) {
  const heading = await page.locator('h1').innerText();
  const match = heading.match(/\b([A-Z0-9]{4})\b/);
  if (!match) throw new Error(`Could not read room code from production lobby heading: ${heading}`);
  return match[1];
}

function delay(milliseconds) { return new Promise((resolve) => setTimeout(resolve, milliseconds)); }
function summary(result) { return { identities: result.identities, acceptedSelfies: result.acceptedSelfies, selfieAudit: result.selfieAudit, failureStatus: result.failureStatus, finalEditAssignments: result.assignments, orphanMedia: result.orphanMedia }; }
