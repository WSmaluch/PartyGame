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

  const displayViewports = [[1024, 640], [1280, 720], [1920, 1080], [2560, 1440]];
  const avatar = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///ywAAAAAAQABAAACAUwAOw==';
  const lobbyMarkup = (playerCount) => `<main class="display-shell"><section class="hero-card hero-card--stage hero-card--lobby"><p class="eyebrow">Ekran gry</p><h1 data-viewport-required>PartyGame</h1><div class="lobby"><div class="room-heading" data-viewport-required><span>Kod pokoju</span><strong>ABCD</strong></div><div class="player-join-qr" data-viewport-required><img src="${avatar}" alt="Kod QR" /><div><strong>Dołącz do gry</strong><a>partygame.local/play/ABCD</a></div></div><p class="lobby-count" data-viewport-required>Gracze: ${playerCount}/10</p><div class="player-grid">${Array.from({ length: playerCount }, (_, index) => `<article class="player-card ${index % 2 ? '' : 'player-card--ready'}" data-viewport-required><img src="${avatar}" alt="Avatar gracza ${index + 1}" /><h3>${['Anna', 'Bardzo długi pseudonim gracza', 'Ola', 'Michał', 'Zosia', 'Krzysztof', 'Maja', 'Jan', 'Natalia', 'Piotr'][index]}</h3><div class="badges"><span class="${index === 0 ? 'host-badge' : 'connected'}">${index === 0 ? 'Host' : 'Online'}</span><span class="${index % 2 ? 'waiting' : 'ready'}">${index % 2 ? 'Czeka' : 'Gotowy/a'}</span></div></article>`).join('')}</div><p class="lobby-hint" data-viewport-required>Dołącz telefonem, zrób zdjęcie profilowe i oznacz gotowość.</p><button class="secondary-action" type="button" data-viewport-required>Zmień pokój</button></div></section></main>`;
  const viewportMetrics = () => {
    const required = [...document.querySelectorAll('[data-viewport-required]')];
    const outside = required.map((element) => ({
      label: element.textContent?.trim().slice(0, 48),
      rect: element.getBoundingClientRect(),
    })).filter(({ rect }) => rect.width <= 0 || rect.height <= 0 || rect.left < -1 || rect.top < -1 || rect.right > window.innerWidth + 1 || rect.bottom > window.innerHeight + 1);
    return {
      scrollHeight: document.documentElement.scrollHeight,
      scrollWidth: document.documentElement.scrollWidth,
      viewportHeight: window.innerHeight,
      outside: outside.map(({ label, rect }) => ({ label, left: rect.left, top: rect.top, right: rect.right, bottom: rect.bottom, width: rect.width, height: rect.height })),
    };
  };
  const displayCases = [
    `<section class="collecting-text-votes"><h2>Pytanie 4 / 5</h2><h1>Bardzo długie pytanie o wspólną podróż, które musi zawinąć się bez wypchnięcia anonimowych odpowiedzi poza ekran telewizora</h1><div class="timer-container"><strong class="timer">24</strong></div><div class="answers-grid">${Array.from({ length: 6 }, (_, i) => `<article class="answer-card"><p>Odpowiedź ${i + 1}: dłuższa anonimowa propozycja widoczna w całości</p></article>`).join('')}</div></section>`,
    `<section class="photo-results"><h1>Prezentacja finałowa</h1><div class="photo-grid">${Array.from({ length: 3 }, (_, i) => `<article class="final-artifact"><div class="photo-placeholder">${i + 1}</div><h3>Gracz ${i + 1} jako kosmiczny pirat</h3></article>`).join('')}</div></section>`,
    `<section class="game-completed"><h1>Gra zakończona</h1><div class="rankings">${Array.from({ length: 3 }, (_, i) => `<article class="ranking-entry"><span class="rank">#${i + 1}</span><span class="name">Gracz ${i + 1}</span><span class="score">${300 - i * 50} pkt</span></article>`).join('')}</div></section>`,
  ];
  for (const [width, height] of displayViewports) for (const content of displayCases) {
    const page = await browser.newPage({ viewport: { width, height } });
    await page.setContent(`<main class="display-shell"><section class="hero-card hero-card--stage hero-card--started"><p class="eyebrow">Ekran gry</p><h1>PartyGame</h1><div class="game-screen-container">${content}</div></section></main>`);
    await page.addStyleTag({ path: path.join(repo, 'apps/display-web/src/styles/global.css') });
    const metrics = await page.evaluate(viewportMetrics);
    if (metrics.scrollWidth > width + 1 || metrics.scrollHeight > height + 2 || metrics.outside.length)
      throw new Error(`Display gameplay does not fit ${width}x${height}: ${JSON.stringify(metrics)}`);
    await page.close();
  }
  for (const [width, height] of displayViewports) for (const playerCount of [1, 3, 6, 10]) {
    const page = await browser.newPage({ viewport: { width, height } });
    await page.setContent(lobbyMarkup(playerCount));
    await page.addStyleTag({ path: path.join(repo, 'apps/display-web/src/styles/global.css') });
    const metrics = await page.evaluate(viewportMetrics);
    if (metrics.scrollWidth > width + 1 || metrics.scrollHeight > height + 2 || metrics.outside.length)
      throw new Error(`Display lobby (${playerCount} players) does not fit ${width}x${height}: ${JSON.stringify(metrics)}`);
    await page.close();
  }
  console.log('Display stage viewport/overflow PASS (gameplay + lobby 1/3/6/10 at 1024x640, 1280x720, 1920x1080, 2560x1440)');
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
