import { createRequire } from 'node:module';
import path from 'node:path';

// CI deliberately installs untrusted browser tooling in RUNNER_TEMP so it cannot
// mutate the retained release artifact under GITHUB_WORKSPACE. Resolve Playwright
// from that isolated package root while keeping this trusted smoke script in-repo.
const playwrightPackageRoot = process.env.RUNNER_TEMP ?? process.cwd();
const require = createRequire(path.join(playwrightPackageRoot, 'package.json'));
const { chromium } = require('playwright');

const target = process.env.DESKTOP_BUDDY_WEB_SMOKE_URL ??
  'http://127.0.0.1:8123/index.html?desktop_buddy_smoke=1';

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage({ viewport: { width: 1280, height: 940 } });
const pageErrors = [];
const consoleLines = [];
const consoleErrors = [];
let runtimeReady = false;
let paintSmokeComplete = false;
let paintSmokeFailure = null;

page.on('pageerror', error => pageErrors.push(String(error)));
page.on('console', message => {
  const text = message.text();
  consoleLines.push(`[${message.type()}] ${text}`);
  if (message.type() === 'error') consoleErrors.push(text);
  if (text.includes('DESKTOP_BUDDY_WEB_RUNTIME_READY')) runtimeReady = true;
  if (text.includes('DESKTOP_BUDDY_WEB_PAINT_SMOKE_COMPLETE')) paintSmokeComplete = true;
  if (text.includes('DESKTOP_BUDDY_WEB_PAINT_SMOKE_FAILED:')) paintSmokeFailure = text;
});

try {
  const response = await page.goto(target, {
    waitUntil: 'domcontentloaded',
    timeout: 120000,
  });
  if (!response || !response.ok()) {
    throw new Error(`HTTP load failed: ${response?.status()}`);
  }

  await page.waitForSelector('canvas', { timeout: 120000 });
  const deadline = Date.now() + 120000;
  while ((!runtimeReady || !paintSmokeComplete) &&
         !paintSmokeFailure &&
         Date.now() < deadline &&
         pageErrors.length === 0) {
    await page.waitForTimeout(250);
  }

  const canvas = await page.locator('canvas').first().evaluate(element => ({
    width: element.width,
    height: element.height,
    clientWidth: element.clientWidth,
    clientHeight: element.clientHeight,
  }));

  if (canvas.width <= 0 || canvas.height <= 0 || canvas.clientWidth <= 0 || canvas.clientHeight <= 0) {
    throw new Error(`Canvas did not render: ${JSON.stringify(canvas)}`);
  }
  if (pageErrors.length) {
    throw new Error(`Browser page errors:\n${pageErrors.join('\n')}`);
  }
  if (paintSmokeFailure) {
    throw new Error(`Paint Buddy browser interaction smoke failed: ${paintSmokeFailure}`);
  }

  const fatalConsole = consoleErrors.filter(line =>
    /abort|exception|uncaught|failed to instantiate|runtimeerror/i.test(line));
  if (fatalConsole.length) {
    throw new Error(`Fatal browser console errors:\n${fatalConsole.join('\n')}`);
  }
  if (!runtimeReady) {
    throw new Error('Encrypted build never reached DESKTOP_BUDDY_WEB_RUNTIME_READY.');
  }
  if (!paintSmokeComplete) {
    throw new Error('Encrypted build did not complete the Paint Buddy Save/New Character/Use/Exit smoke.');
  }

  console.log(`Hardened itch browser smoke passed: canvas=${JSON.stringify(canvas)}`);
} catch (error) {
  console.error('Last browser log lines:');
  for (const line of consoleLines.slice(-160)) console.error(line);
  throw error;
} finally {
  await browser.close();
}
