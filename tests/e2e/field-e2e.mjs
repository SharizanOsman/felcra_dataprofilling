// Step 4 end-to-end: field app on a phone-sized screen. The QR scanner is fed a fake camera
// showing a real ear-tag label, so the actual decode path is exercised.
import { chromium } from 'playwright-core';
import { inEnglish } from './english.mjs';
import fs from 'node:fs';
import path from 'node:path';

const root = 'http://localhost:5237';
const dir = process.argv[2] ?? '.';
const results = [];
const visible = async (l, timeout = 15000) => { try { await l.waitFor({ state: 'visible', timeout }); return true; } catch { return false; } };
const check = (name, ok, extra = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); };
const errors = [];

// ---- 1. Pick an animal and render its label QR into a fake-camera video (MJPEG) -------------------
const api = async (url, opts = {}) => fetch(`${root}${url}`, opts);
const login = await (await api('/api/auth/login', { method: 'POST', headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ email: 'admin@felcra.local', password: 'Felcra@2026' }) })).json();
const auth = { Authorization: `Bearer ${login.accessToken}` };
const target = (await (await api('/api/animals?search=232&pageSize=5', { headers: auth })).json()).items.find(a => a.tagNumber === '232');
console.log('target', target.animalNo, 'tag', target.tagNumber);

const helper = inEnglish(await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true }));
const adminPage = await (await helper.newContext({ viewport: { width: 1200, height: 900 } })).newPage();
await adminPage.goto(`${root}/admin/login`);
await adminPage.getByLabel('Email').fill('admin@felcra.local');
await adminPage.getByLabel('Password').fill('Felcra@2026');
await adminPage.getByRole('button', { name: 'Sign in' }).click();
await adminPage.getByRole('heading', { name: 'Dashboard' }).waitFor();
await adminPage.goto(`${root}/admin/animals/${target.id}`);
const qrPng = await adminPage.locator('.animal-qr').screenshot();
// Frame: white 640x480 with the QR centred (plus quiet zone), saved as JPEG frames.
await adminPage.setContent(`<body style="margin:0;width:640px;height:480px;background:#fff;display:flex;align-items:center;justify-content:center">
  <img src="data:image/png;base64,${qrPng.toString('base64')}" style="width:300px;height:300px;image-rendering:pixelated"></body>`);
await adminPage.setViewportSize({ width: 640, height: 480 });
const frame = await adminPage.screenshot({ type: 'jpeg', quality: 90 });
const video = path.join(dir, 'fake-camera.mjpeg');
fs.writeFileSync(video, Buffer.concat(Array(60).fill(frame)));
await helper.close();

// ---- 2. Field app with the fake camera --------------------------------------------------------------
const browser = inEnglish(await chromium.launch({
  channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true,
  args: ['--use-fake-ui-for-media-stream', '--use-fake-device-for-media-stream', `--use-file-for-fake-video-capture=${video}`],
}));
const context = await browser.newContext({ viewport: { width: 400, height: 820 }, isMobile: true, hasTouch: true, permissions: ['camera'] });
const page = await context.newPage();
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
page.on('pageerror', e => errors.push(e.message));

try {
  await page.goto(`${root}/field/login`);
  await page.getByLabel('Email').fill('officer@felcra.local');
  await page.getByLabel('Password').fill('Felcra@2026');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByText('Hello,').waitFor();

  await page.getByRole('link', { name: 'Scan ear tag' }).click();
  check('Scanning the QR label opens the animal', await visible(page.getByText(`Tag 232 · ${target.animalNo}`), 25000));
  await page.screenshot({ path: path.join(dir, 'field-animal-page.png') });

  // Weight: implausible value is refused, then a real one saved
  await page.getByRole('link', { name: /Weight/ }).click();
  await page.getByLabel('Weight (kg)').fill('2000');
  await page.getByRole('button', { name: 'Save' }).click();
  check('Implausible weight shows a message', await visible(page.getByText(/must be between 1 and 1500/)));
  await page.getByLabel('Weight (kg)').fill('362');
  await page.getByRole('button', { name: 'Save' }).click();
  check('Weight saved and shown in history', await visible(page.getByText('Weight 362 kg').first()));

  await page.getByRole('link', { name: /Body condition/ }).click();
  await page.getByLabel('BCS').fill('3.5');
  await page.getByRole('button', { name: 'Save' }).click();
  check('BCS saved', await visible(page.getByText('BCS 3.5').first()));

  // Treatment with product defaults
  await page.getByRole('link', { name: /Treatment/ }).click();
  await page.getByLabel('Treatment').click();
  await page.getByRole('option', { name: /FMD vaccine/ }).click();
  await page.getByLabel('Batch / lot number').fill('FMD-2026-07');
  await page.getByRole('button', { name: 'Save' }).click();
  check('Treatment saved', await visible(page.getByText('FMD vaccine (booster)').first()));

  // Observation with a photo (resized in the browser, uploaded, shown as thumbnail)
  await page.getByRole('link', { name: /Observation/ }).click();
  await page.locator('.mud-select').first().click();
  await page.getByRole('option', { name: 'Legs and hooves' }).click();
  await page.getByLabel('What did you see?').fill('Slight limp on left hind leg');
  await page.locator('input[type=file]').setInputFiles({ name: 'leg.jpg', mimeType: 'image/jpeg', buffer: frame });
  check('Photo preview appears after upload', await visible(page.locator('img[alt="Photo"]')));
  await page.getByRole('button', { name: 'Save' }).click();
  check('Observation saved with photo thumbnail', await visible(page.getByText('Slight limp on left hind leg').first()) && await visible(page.locator('.mud-timeline img').first()));
  await page.screenshot({ path: path.join(dir, 'field-history.png'), fullPage: true });

  // Find by tag with duplicates
  await page.goto(`${root}/field/find`);
  await page.getByLabel('Tag number or animal no.').fill('08478');
  check('Duplicate tag search warns and lists both animals', await visible(page.getByText('Several animals share this tag')) &&
    (await page.locator('.mud-list-item').count()) === 2);
  await page.screenshot({ path: path.join(dir, 'field-find.png') });

  // Home shows recently opened
  await page.goto(`${root}/field/`);
  check('Home lists the recently opened animal', await visible(page.getByText('Tag 232')));

  // Admin sees the field records
  const admin = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
  await admin.goto(`${root}/admin/login`);
  await admin.getByLabel('Email').fill('manager@felcra.local');
  await admin.getByLabel('Password').fill('Felcra@2026');
  await admin.getByRole('button', { name: 'Sign in' }).click();
  await admin.getByRole('heading', { name: 'Dashboard' }).waitFor();
  await admin.goto(`${root}/admin/animals/${target.id}`);
  check('Admin shows field records', await visible(admin.getByText(/Measurements \(\d+\)/)));
  check('Admin animal list shows the new latest weight', await (async () => {
    await admin.goto(`${root}/admin/animals`);
    await admin.getByPlaceholder('Tag or animal no.').fill('232');
    return visible(admin.locator('tr', { hasText: target.animalNo }).getByText('362 kg'));
  })());
} catch (err) {
  check('Unexpected error', false, String(err).split('\n')[0]);
} finally {
  await browser.close();
}

if (errors.length) console.log('Browser errors:\n  ' + errors.join('\n  '));
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} checks passed`);
process.exit(failed ? 1 : 0);
