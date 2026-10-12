// Step 9: smoke test of the production stack behind Caddy (HTTPS): admin, reports, offline field app, push.
import { chromium } from 'playwright-core';
import { inEnglish } from './english.mjs';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const root = process.argv[2] ?? 'https://localhost:8443';
const dir = process.argv[3] ?? '.';
const results = [];
const visible = async (l, timeout = 20000) => { try { await l.waitFor({ state: 'visible', timeout }); return true; } catch { return false; } };
const check = (name, ok, extra = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); };
const errors = [];

const browser = inEnglish(await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true, args: ['--ignore-certificate-errors'] }));
try {
  // ---- Admin over HTTPS -----------------------------------------------------------------------------
  const admin = await (await browser.newContext({ ignoreHTTPSErrors: true, viewport: { width: 1400, height: 1000 }, acceptDownloads: true })).newPage();
  admin.on('console', m => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) errors.push(`admin: ${m.text()}`); });
  await admin.goto(`${root}/admin/login`);
  await admin.getByLabel('Email').fill('manager@felcra.local');
  await admin.getByLabel('Password').fill('Felcra@2026');
  await admin.getByRole('button', { name: 'Sign in' }).click();
  const active = admin.locator('[data-testid=kpi]').filter({ hasText: 'Active animals' }).locator('[data-testid=kpi-value]');
  check('Dashboard over HTTPS', await visible(active) && (await active.innerText()) === '140');
  await admin.getByRole('link', { name: 'Reports' }).click();
  const [pdf] = await Promise.all([admin.waitForEvent('download'), admin.getByRole('button', { name: 'Census and profiling form PDF' }).click()]);
  check('Census form PDF from the production container', pdf.suggestedFilename() === 'census-form-pagoh-2026-07-02.pdf');
  await admin.context().close();

  // ---- Field app: installable / offline, then push ---------------------------------------------------
  // A persistent profile (not incognito-like): browsers disable the Push API in incognito windows.
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'felcra-field-'));
  const context = inEnglish(await chromium.launchPersistentContext(profile, {
    channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true, args: ['--ignore-certificate-errors'], ignoreHTTPSErrors: true,
    viewport: { width: 400, height: 860 }, isMobile: true, hasTouch: true,
  }));
  await context.grantPermissions(['notifications'], { origin: root });
  const page = context.pages()[0] ?? await context.newPage();
  page.on('console', m => { if (m.type() === 'error' && !/Failed to load resource|ERR_INTERNET_DISCONNECTED/.test(m.text())) errors.push(`field: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`field: ${e.message}`));
  await page.goto(`${root}/field/login`);
  await page.getByLabel('Email').fill('officer@felcra.local');
  await page.getByLabel('Password').fill('Felcra@2026');
  await page.getByRole('button', { name: 'Sign in' }).click();
  check('Field app downloads the farm', await visible(page.getByText(/140 animals on this phone/), 60000));

  await page.waitForFunction(() => navigator.serviceWorker.controller !== null, null, { timeout: 30000 }).catch(() => {});
  if (!(await page.evaluate(() => !!navigator.serviceWorker.controller))) { await page.reload(); await page.getByText(/animals on this phone/).waitFor(); }
  check('Service worker controls the page', await page.evaluate(() => !!navigator.serviceWorker.controller));
  const fontCached = await page.evaluate(async () => (await Promise.all((await caches.keys()).map(async k => (await (await caches.open(k)).keys()).some(r => r.url.endsWith('.woff2'))))).some(Boolean));
  check('Self-hosted fonts are in the offline cache', fontCached);

  await context.setOffline(true);
  await page.reload();
  check('Field app opens offline', await visible(page.getByText(/140 animals on this phone/)));
  await context.setOffline(false);
  await page.reload();
  await page.getByText(/140 animals on this phone/).waitFor();

  await page.getByRole('link', { name: 'Sync status' }).click();
  const state = page.locator('[data-testid=notification-state]');
  check('Notification settings shown', await visible(state), await state.innerText().catch(() => ''));
  const turnOn = page.getByRole('button', { name: 'Turn on' });
  if (await visible(turnOn, 10000)) {
    await turnOn.click();
    const on = await visible(page.getByText('Task notifications are on.'), 30000);
    check('Notifications turned on (browser subscribed, server registered)', on,
      on ? '' : await page.locator('.mud-snackbar').allInnerTexts().then(t => t.join(' | ')));
    if (on) {
      await page.getByRole('button', { name: 'Send a test' }).click();
      check('Test push accepted by the push service', await visible(page.getByText(/Test sent/), 30000),
        await page.locator('.mud-snackbar').allInnerTexts().then(t => t.join(' | ')));
      await page.getByRole('button', { name: 'Turn off' }).click();
      check('Notifications turned off', await visible(page.getByRole('button', { name: 'Turn on' })));
    }
  }
  await page.screenshot({ path: path.join(dir, 'prod-sync.png'), fullPage: true });
  await context.close();
} catch (e) {
  check('No unexpected exception', false, e.message);
} finally {
  await browser.close();
}

check('No browser errors (incl. Content-Security-Policy violations)', errors.length === 0, errors.slice(0, 3).join(' | '));
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
