// Step 6 end-to-end: breeding in the field app (online and offline) and the admin Breeding page.
import { chromium } from 'playwright-core';
import path from 'node:path';

const root = process.argv[2] ?? 'http://localhost:5237';
const dir = process.argv[3] ?? '.';
const results = [];
const visible = async (l, timeout = 15000) => { try { await l.waitFor({ state: 'visible', timeout }); return true; } catch { return false; } };
const check = (name, ok, extra = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); };
const errors = [];

const fmt = d => d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }); // "01 Sep 2026"
const daysAgo = n => { const d = new Date(); d.setDate(d.getDate() - n); return d; };

// Animals from the Pagoh census (via the API, as the manager).
const login = async email => ({ Authorization: `Bearer ${(await (await fetch(`${root}/api/auth/login`, { method: 'POST',
  headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email, password: 'Felcra@2026' }) })).json()).accessToken}` });
const manager = await login('manager@felcra.local');
const animals = (await (await fetch(`${root}/api/animals?pageSize=200`, { headers: manager })).json()).items;
const tag = t => animals.find(a => a.tagNumber === t);
const dam1 = tag('232');   // Dam, line 5
const dam2 = tag('2321');  // Dam
const dam3 = tag('2348');  // Dam, recorded offline
const bull = tag('178');   // Bull
const calfTag = `C${Date.now() % 100000}`;

const browser = await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true });
const context = await browser.newContext({ viewport: { width: 400, height: 860 }, isMobile: true, hasTouch: true });
const page = await context.newPage();
page.on('console', m => { if (m.type() === 'error' && !/Failed to load resource|ERR_INTERNET_DISCONNECTED/.test(m.text())) errors.push(m.text()); });
page.on('pageerror', e => errors.push(e.message));
const go = async route => { await page.evaluate(r => Blazor.navigateTo(r), route); await page.waitForTimeout(300); };
const setDate = async value => {
  const input = page.getByLabel('Date', { exact: true });
  await input.fill(value);
  await input.press('Enter');
  await page.keyboard.press('Escape');
};
// Exact match: the status chip ("Served · waiting for pregnancy check") also contains event names.
const chooseEvent = async name => page.locator('.mud-chip').filter({ hasText: new RegExp(`^\\s*${name.replace(/[()]/g, '\\$&')}\\s*$`) }).first().click();
const save = async () => page.getByRole('button', { name: 'Save' }).click();
const waitHistory = () => page.getByText('History', { exact: true }).waitFor();

try {
  await page.goto(`${root}/field/login`);
  await page.getByLabel('Email').fill('officer@felcra.local');
  await page.getByLabel('Password').fill('Felcra@2026');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByText(/140 animals on this phone/).waitFor({ timeout: 30000 });

  // ---- AI, then the pregnancy-check task appears -------------------------------------------------
  await go(`a/${dam1.id}`);
  await waitHistory();
  await page.getByRole('link', { name: /Breeding/ }).click();
  await chooseEvent('AI (insemination)');
  await setDate(fmt(daysAgo(50)));
  await page.getByLabel('Semen / straw code').fill('BRH-2026-01');
  await save();
  check('AI saved and shown with semen code', await visible(page.getByText('AI (insemination)').first()) && await visible(page.getByText(/semen BRH-2026-01/).first()));
  await page.waitForTimeout(2500);
  await go('');
  check('Home shows an overdue pregnancy check task', await visible(page.locator('.mud-list-item', { hasText: 'Pregnancy check due' }).filter({ hasText: 'Tag 232' })) &&
    await visible(page.getByText('overdue').first()));

  // ---- Positive pregnancy check -------------------------------------------------------------------
  await page.locator('.mud-list-item', { hasText: 'Pregnancy check due' }).filter({ hasText: 'Tag 232' }).click();
  await waitHistory();
  await page.getByRole('link', { name: /Breeding/ }).click();
  await chooseEvent('Pregnancy check');
  await page.getByText('Pregnant', { exact: true }).click();
  await save();
  const due = new Date(daysAgo(50)); due.setDate(due.getDate() + 285);
  check('Dam shows as pregnant with calving due date', await visible(page.getByText(`Pregnant · calving due ${fmt(due)}`), 20000), fmt(due));
  await page.screenshot({ path: path.join(dir, 'breeding-pregnant.png') });

  // ---- Validation: calving without a calf -----------------------------------------------------------
  await go(`a/${dam2.id}/breeding`);
  await chooseEvent('Calving');
  await page.getByRole('button', { name: 'Remove calf' }).click();
  await save();
  check('Calving without a calf is refused with a message', await visible(page.getByText(/Add the calf/)));

  // ---- Calving registers a calf ----------------------------------------------------------------------
  await page.getByRole('button', { name: 'Add calf' }).click();
  await page.getByLabel('Ear tag (if tagged)').fill(calfTag);
  await page.getByLabel('Birth weight (kg)').fill('31');
  await page.getByText('Assisted calving').click();
  await save();
  check('Calving saved', await visible(page.getByText(/Calving — 1 calf, assisted/).first()));
  await page.screenshot({ path: path.join(dir, 'breeding-calving.png'), fullPage: true });

  await page.waitForTimeout(3000);
  await page.getByRole('link', { name: 'Sync status' }).click();
  await page.getByRole('button', { name: 'Sync now' }).click();
  await page.waitForTimeout(2500);
  await go('find');
  await page.getByLabel('Tag number or animal no.').fill(calfTag);
  check('New calf reaches the phone and opens with its dam as female calf', await visible(page.getByText(`Tag ${calfTag}`).first()) &&
    await visible(page.getByText('Female calf (AB)').first()));

  // ---- Bull soundness exam -----------------------------------------------------------------------
  await go(`a/${bull.id}`);
  await waitHistory();
  await page.getByRole('link', { name: /Soundness exam/ }).click();
  await page.getByText('Satisfactory', { exact: true }).click();
  await save();
  check('Bull soundness exam saved', await visible(page.getByText(/Bull soundness exam — satisfactory/).first()));

  // ---- Offline breeding record ---------------------------------------------------------------------
  await context.setOffline(true);
  await go(`a/${dam3.id}/breeding`);
  await chooseEvent('Heat');
  await save();
  check('Heat recorded offline, waiting to sync', await visible(page.getByText('Waiting to sync').first()));
  await context.setOffline(false);
  await page.waitForTimeout(4000);
  const dam3Breeding = await (await fetch(`${root}/api/animals/${dam3.id}/breeding`, { headers: manager })).json();
  check('Offline heat reached the server', dam3Breeding.events.some(e => e.type === 1));

  // ---- Admin ---------------------------------------------------------------------------------------
  const admin = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
  await admin.goto(`${root}/admin/login`);
  await admin.getByLabel('Email').fill('manager@felcra.local');
  await admin.getByLabel('Password').fill('Felcra@2026');
  await admin.getByRole('button', { name: 'Sign in' }).click();
  await admin.getByRole('heading', { name: 'Dashboard' }).waitFor();
  await admin.getByRole('link', { name: 'Breeding' }).click();
  const row = admin.locator('tr', { hasText: 'PAGOH' }).first();
  await row.waitFor();
  const cells = (await row.locator('td').allInnerTexts()).map(t => t.trim());
  // Farm, females, pregnant, served, open, no records, pregnancy rate, interval, calves, calving rate, abortions
  check('Herd figures: 1 pregnant, 1 calf in 12 months', cells[2] === '1' && cells[8] === '1', cells.join(' | '));
  await admin.screenshot({ path: path.join(dir, 'admin-breeding.png'), fullPage: true });

  await admin.goto(`${root}/admin/animals/${dam2.id}`);
  await admin.getByRole('tab', { name: /Breeding/ }).click();
  check('Admin animal page shows the calving and links the calf', await visible(admin.getByText(/Calving — 1 calf, assisted/)) &&
    await visible(admin.getByText(new RegExp(calfTag)).first()));
} catch (err) {
  check('Unexpected error', false, String(err).split('\n').slice(0, 3).join(' / '));
  await page.screenshot({ path: path.join(dir, 'breeding-failure.png'), fullPage: true }).catch(() => {});
} finally {
  await browser.close();
}

if (errors.length) console.log('Browser errors:\n  ' + errors.join('\n  '));
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} checks passed`);
process.exit(failed ? 1 : 0);
