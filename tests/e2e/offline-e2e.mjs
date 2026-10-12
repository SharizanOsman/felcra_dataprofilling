// Step 5 end-to-end: field app working with no connection (published build, service worker active).
// The browser is taken offline with Playwright; the server is checked directly from Node.
import { chromium } from 'playwright-core';
import { inEnglish } from './english.mjs';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2] ?? 'http://localhost:5299';
const dir = process.argv[3] ?? '.';
const results = [];
const visible = async (l, timeout = 20000) => { try { await l.waitFor({ state: 'visible', timeout }); return true; } catch { return false; } };
const check = (name, ok, extra = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); };
const errors = [];

// ---- Server helpers (Node is never offline) ------------------------------------------------------
async function token(email) {
  const r = await fetch(`${root}/api/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ email, password: 'Felcra@2026' }) });
  return { Authorization: `Bearer ${(await r.json()).accessToken}` };
}
const manager = await token('manager@felcra.local');
const get = async url => (await fetch(`${root}${url}`, { headers: manager })).json();
const animals = (await get('/api/animals?pageSize=200&sort=animalNo')).items;
const byTag = tag => animals.find(a => a.tagNumber === tag);
const target = byTag('232');
const batch = animals.filter(a => a.tagNumber && a.id !== target.id && !['08478', '107', '135', '136', '0088'].includes(a.tagNumber)).slice(0, 12);
const toReject = animals.find(a => a.tagNumber === '0088');
const records = async id => get(`/api/animals/${id}/records`);

for (const a of [target, ...batch, toReject]) {
  if ((await records(a.id)).measurements.length) throw new Error(`Animal ${a.animalNo} already has measurements; reset the database first.`);
}

// ---- Browser --------------------------------------------------------------------------------------
const browser = inEnglish(await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true }));
const context = await browser.newContext({ viewport: { width: 400, height: 820 }, isMobile: true, hasTouch: true });
const page = await context.newPage();
page.on('console', m => { if (m.type() === 'error' && !/Failed to load resource|ERR_INTERNET_DISCONNECTED|Failed to fetch/.test(m.text())) errors.push(m.text()); });
page.on('pageerror', e => errors.push(e.message));
const go = async route => {
  await page.evaluate(r => Blazor.navigateTo(r), route);
  await page.waitForTimeout(300);
};

try {
  // Sign in online and let the app download the farm and register its service worker.
  await page.goto(`${root}/field/login`);
  await page.getByLabel('Email').fill('officer@felcra.local');
  await page.getByLabel('Password').fill('Felcra@2026');
  await page.getByRole('button', { name: 'Sign in' }).click();
  check('Farm data downloaded to the phone', await visible(page.getByText('140 animals on this phone'), 30000));
  await page.evaluate(() => navigator.serviceWorker.ready);
  await page.reload();
  await page.getByText('140 animals on this phone').waitFor({ timeout: 30000 });
  check('Service worker controls the page', await page.evaluate(() => !!navigator.serviceWorker.controller));

  // ---- Offline --------------------------------------------------------------------------------
  await context.setOffline(true);
  check('Offline banner appears', await visible(page.getByText(/Offline — records are saved on this phone/)));
  await page.reload();
  check('App opens while offline, still signed in', await visible(page.getByText('140 animals on this phone')));

  await go('find');
  await page.getByLabel('Tag number or animal no.').fill('232');
  check('Find by tag works offline (opens the animal)', await visible(page.getByText(`Tag 232 · ${target.animalNo}`)));
  check('Latest census weight is shown offline', await visible(page.getByText(/345 kg · 02 Jul/)));

  await page.getByRole('link', { name: /Weight/ }).click();
  await page.getByLabel('Weight (kg)').fill('377');
  await page.getByRole('button', { name: 'Save' }).click();
  check('Weight saved on the phone with offline message', await visible(page.getByText(/saved on this phone/).first()));
  check('Timeline marks it "Waiting to sync"', await visible(page.getByText('Waiting to sync').first()));

  await page.getByRole('link', { name: /Treatment/ }).click();
  await page.locator('.mud-select').first().click();
  await page.getByRole('option', { name: /Doramectin/ }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  await page.getByText('Doramectin (Dectomax)').first().waitFor();

  await page.getByRole('link', { name: /Observation/ }).click();
  await page.getByLabel('What did you see?').fill('Recorded offline with a photo');
  await page.locator('input[type=file]').setInputFiles({ name: 'p.jpg', mimeType: 'image/jpeg', buffer: fs.readFileSync(path.join(dir, 'fake-camera.mjpeg')).subarray(0, 60000) });
  await page.locator('img[alt="Photo"]').waitFor();
  await page.getByRole('button', { name: 'Save' }).click();
  await page.getByText(/General \(1 photo\)/).first().waitFor();

  // A dozen more animals, as at a weighing session.
  for (const [i, a] of batch.entries()) {
    await go(`a/${a.id}/measure/weight`);
    await page.getByLabel('Weight (kg)').fill(String(200 + i));
    await page.getByRole('button', { name: 'Save' }).click();
    await page.getByText('History', { exact: true }).waitFor();
  }
  await go(`a/${toReject.id}/measure/weight`);
  await page.getByLabel('Weight (kg)').fill('455');
  await page.getByRole('button', { name: 'Save' }).click();
  await page.getByText('History', { exact: true }).waitFor();

  await go('sync');
  const expectedPending = 3 + batch.length + 1;
  check(`Sync page lists ${expectedPending} records waiting`, (await page.getByText('Waiting to send').count()) === expectedPending);
  await page.screenshot({ path: path.join(dir, 'offline-sync-page.png'), fullPage: true });

  const serverBefore = (await records(target.id)).measurements.length + (await records(batch[0].id)).measurements.length;
  check('Nothing reached the server while offline', serverBefore === 0);

  // Meanwhile at HQ: one animal is sold, and a new calf is registered.
  const sold = await (await fetch(`${root}/api/animals/${toReject.id}`, { headers: manager })).json();
  await fetch(`${root}/api/animals/${toReject.id}/status`, { method: 'POST', headers: { ...manager, 'content-type': 'application/json' },
    body: JSON.stringify({ status: 2, date: '2026-10-01', version: sold.version }) });
  const farmId = target.farmId;
  const calfTag = `CALF${Date.now() % 100000}`;
  await fetch(`${root}/api/animals`, { method: 'POST', headers: { ...manager, 'content-type': 'application/json' },
    body: JSON.stringify({ farmId, tagNumber: calfTag, category: 4, sex: 2 }) });

  // ---- Back online ----------------------------------------------------------------------------
  await context.setOffline(false);
  check('Records are sent when the connection returns', await visible(page.getByText(/is sold; records can only be added/), 30000) &&
    await (async () => { for (let i = 0; i < 40 && (await page.getByText('Waiting to send').count()) > 0; i++) await page.waitForTimeout(500); return (await page.getByText('Waiting to send').count()) === 0; })());

  const t = await records(target.id);
  check('Server has the weight, treatment and observation with photo',
    t.measurements.some(m => m.value === 377) && t.treatments.length === 1 && t.observations.length === 1 && t.observations[0].photoIds.length === 1);
  let batchOk = 0;
  for (const [i, a] of batch.entries()) if ((await records(a.id)).measurements.some(m => m.value === 200 + i)) batchOk++;
  check(`All ${batch.length} batch weights arrived exactly once`, batchOk === batch.length &&
    (await Promise.all(batch.map(a => records(a.id)))).every(r => r.measurements.length === 1));

  check('Weight for the sold animal is flagged, not lost', await visible(page.getByText(/is sold; records can only be added for active animals/)));
  await page.screenshot({ path: path.join(dir, 'offline-rejected.png'), fullPage: true });
  await page.locator('.mud-list-item button').last().click();
  await page.getByRole('dialog').getByRole('button', { name: 'Discard' }).click();
  check('Rejected record can be discarded', await visible(page.getByText('Everything recorded on this phone has been sent.')));

  // The calf registered at HQ reaches the phone.
  await go('find');
  await page.getByLabel('Tag number or animal no.').fill(calfTag);
  check('New animal from HQ is on the phone after sync', await visible(page.getByText(`Tag ${calfTag}`)));

  // Offline again: the calf can be found without a connection.
  await context.setOffline(true);
  await go('');
  await go('find');
  await page.getByLabel('Tag number or animal no.').fill(calfTag);
  check('…and can be found offline', await visible(page.getByText(`Tag ${calfTag}`)));
  await context.setOffline(false);
} catch (err) {
  check('Unexpected error', false, String(err).split('\n')[0]);
} finally {
  await browser.close();
}

if (errors.length) console.log('Browser errors:\n  ' + errors.join('\n  '));
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} checks passed`);
process.exit(failed ? 1 : 0);
