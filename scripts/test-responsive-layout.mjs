#!/usr/bin/env node
import { createRequire } from 'node:module';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const requireFromDisplay = createRequire(path.join(repo, 'apps/display-web/package.json'));
const { chromium, webkit } = requireFromDisplay('@playwright/test');
const viewports = [
  [320, 568], [375, 667], [390, 844], [430, 932],
  [768, 1024], [1366, 768], [1920, 1080],
];

const browser = await chromium.launch({ headless: true });
try {
  for (const [width, height] of viewports) {
    const page = await browser.newPage({ viewport: { width, height } });
    await page.setContent(`<main class="page-shell"><section class="card game-card"><header class="game-header"><p class="eyebrow">PYTANIE 4/5</p><p class="connection connection--connected"><span class="connection-dot"></span>Połączono</p></header><div class="stage-content"><h2>Kto z tej bardzo długiej grupy najprawdopodobniej rozbawi wszystkich podczas wspólnego wyjazdu?</h2><fieldset><legend>Wybierz gracza</legend><div class="option-list"><button class="player-option"><span class="avatar avatar--placeholder">👤</span><span>Bardzo długi polski pseudonim gracza</span><span class="selection-check">✓</span></button><button class="player-option"><span class="avatar avatar--placeholder">👤</span><span>Ania</span><span class="selection-check">✓</span></button></div><button type="button">Wyślij odpowiedź</button></fieldset></div><p class="countdown">Pozostały czas: <span>9.8s</span></p></section></main>`);
    await page.addStyleTag({ path: path.join(repo, 'apps/player-web/src/styles.css') });
    const metrics = await page.evaluate(() => {
      const progress = document.querySelector('.eyebrow').getBoundingClientRect();
      const connection = document.querySelector('.connection').getBoundingClientRect();
      const overlap = Math.max(0, Math.min(progress.right, connection.right) - Math.max(progress.left, connection.left)) * Math.max(0, Math.min(progress.bottom, connection.bottom) - Math.max(progress.top, connection.top));
      const button = document.querySelector('fieldset > button').getBoundingClientRect();
      return { overlap, scrollWidth: document.documentElement.scrollWidth, buttonBottom: button.bottom, bodyHeight: document.body.scrollHeight };
    });
    if (metrics.overlap !== 0) throw new Error(`Player header overlaps at ${width}x${height}`);
    if (metrics.scrollWidth > width) throw new Error(`Player scrolls horizontally at ${width}x${height}: ${metrics.scrollWidth}`);
    if (metrics.buttonBottom > metrics.bodyHeight + 1) throw new Error(`Player CTA is clipped at ${width}x${height}`);

    await page.setContent(`<main class="display-shell"><section class="hero-card"><p class="eyebrow">Ekran gry</p><h1>PartyGame</h1><div class="game-screen-container stage-transition"><div class="question-intro"><h2>Pytanie 4 / 5</h2><h1>Kto najprawdopodobniej zapomni najważniejszej rzeczy podczas bardzo długiej wspólnej podróży?</h1><div class="timer-container"><strong class="timer timer--urgent">5s</strong></div></div></div></section></main>`);
    await page.addStyleTag({ path: path.join(repo, 'apps/display-web/src/styles/global.css') });
    const displayWidth = await page.evaluate(() => document.documentElement.scrollWidth);
    if (displayWidth > width) throw new Error(`Display scrolls horizontally at ${width}x${height}: ${displayWidth}`);
    await page.close();
  }

  const reduced = await browser.newPage({ viewport: { width: 390, height: 844 }, reducedMotion: 'reduce' });
  await reduced.setContent('<main class="page-shell"><section class="card"><div class="stage-content">Question</div><p class="countdown countdown--urgent"><span>4.0s</span></p></section></main>');
  await reduced.addStyleTag({ path: path.join(repo, 'apps/player-web/src/styles.css') });
  const reducedAnimations = await reduced.evaluate(() => [getComputedStyle(document.querySelector('.stage-content')).animationName, getComputedStyle(document.querySelector('.countdown span')).animationName]);
  if (reducedAnimations.some((name) => name !== 'none')) throw new Error(`Reduced motion still animates: ${reducedAnimations.join(', ')}`);
  await reduced.close();
  console.log(`Responsive layout tests PASS (${viewports.length} viewports + reduced motion)`);
} finally {
  await browser.close();
}

const mobileWebKit = await webkit.launch({ headless: true });
try {
  for (const [width, height] of viewports.slice(0, 4)) {
    const page = await mobileWebKit.newPage({ viewport: { width, height }, isMobile: true, hasTouch: true });
    await page.setContent(`<meta name="viewport" content="width=device-width, initial-scale=1.0"><main class="page-shell"><section class="card game-card"><header class="game-header"><p class="eyebrow">PYTANIE 4/5</p><p class="connection connection--connected">Połączono</p></header><div class="stage-content"><h2>Narysuj odpowiedź</h2><section class="drawing-editor"><canvas class="drawing-canvas" role="img" aria-label="Obszar rysowania" width="1024" height="1024"></canvas><div class="drawing-tools drawing-tools--colors">${Array.from({ length: 10 }, (_, index) => `<button type="button" aria-label="Kolor ${index + 1}"></button>`).join('')}</div><div class="drawing-tools">${[4, 10, 22].map((line) => `<button type="button">${line}</button>`).join('')}<button type="button">Gumka</button></div><div class="drawing-tools"><button type="button">Cofnij ruch</button><button type="button">Ponów ruch</button><button type="button">Wyczyść rysunek</button></div></section><button id="drawing-submit" type="button">Wyślij rysunek</button></div><p class="countdown">Pozostały czas: <span>9.8s</span></p></section></main>`);
    await page.addStyleTag({ path: path.join(repo, 'apps/player-web/src/styles.css') });
    const metrics = await page.evaluate(() => {
      const canvas = document.querySelector('.drawing-canvas').getBoundingClientRect();
      const toolbar = document.querySelector('.drawing-tools--colors').getBoundingClientRect();
      const submit = document.querySelector('#drawing-submit').getBoundingClientRect();
      return { canvasWidth: canvas.width, canvasHeight: canvas.height, toolbarWidth: toolbar.width, toolbarHeight: toolbar.height, submitWidth: submit.width, submitHeight: submit.height, scrollWidth: document.documentElement.scrollWidth };
    });
    if (!metrics.canvasWidth || !metrics.canvasHeight || !metrics.toolbarWidth || !metrics.toolbarHeight || !metrics.submitWidth || !metrics.submitHeight)
      throw new Error(`WebKit Drawing control has zero geometry at ${width}x${height}: ${JSON.stringify(metrics)}`);
    if (metrics.scrollWidth > width) throw new Error(`WebKit Drawing scrolls horizontally at ${width}x${height}: ${metrics.scrollWidth}`);
    await page.close();
  }
  console.log('WebKit mobile Drawing layout PASS (320x568, 375x667, 390x844, 430x932)');
} finally {
  await mobileWebKit.close();
}
