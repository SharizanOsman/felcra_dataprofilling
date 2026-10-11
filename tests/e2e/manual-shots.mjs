// Screenshots for the user manuals (docs/manual/images), taken on the Pagoh demo data.
import { chromium } from 'playwright-core';
import fs from 'node:fs';
import path from 'node:path';

const root = 'http://localhost:5237';
const out = process.argv[2];
fs.mkdirSync(out, { recursive: true });
const shot = async (page, name, opts = {}) => { await page.waitForTimeout(600); await page.screenshot({ path: path.join(out, `${name}.png`), ...opts }); console.log('shot', name); };

// Some activity so the screens are not empty: weights, a treatment, an AI service, a pregnancy check.
const token = async email => (await (await fetch(`${root}/api/auth/login`, { method: 'POST', headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ email, password: 'Felcra@2026' }) })).json()).accessToken;
const officer = await token('officer@felcra.local');
const post = (url, body) => fetch(`${root}${url}`, { method: 'POST', headers: { 'content-type': 'application/json', Authorization: `Bearer ${officer}` }, body: JSON.stringify(body) });
const animals = (await (await fetch(`${root}/api/animals?pageSize=200`, { headers: { Authorization: `Bearer ${officer}` } })).json()).items;
const byTag = t => animals.find(a => a.tagNumber === t);
const day = n => { const d = new Date(Date.now() + 8 * 3600e3); d.setDate(d.getDate() - n); return d.toISOString().slice(0, 10); };
const products = await (await fetch(`${root}/api/treatment-products`, { headers: { Authorization: `Bearer ${officer}` } })).json();
const dam = byTag("232");
if (!process.env.SKIP_SEED) {
await post(`/api/animals/${dam.id}/measurements`, { type: 1, value: 352, measuredOn: day(3), method: 1 });
await post(`/api/animals/${dam.id}/measurements`, { type: 2, value: 3, measuredOn: day(3) });
await post(`/api/animals/${dam.id}/treatments`, { treatmentProductId: products.find(p => p.name.startsWith('Doramectin')).id, givenOn: day(3), dose: 5, doseUnit: 'ml', route: 2 });
await post(`/api/animals/${dam.id}/breeding-events`, { type: 2, eventDate: day(50) });
for (const [t, kg] of [['117', 131], ['111', 150], ['192', 126], ['0125', 128]]) await post(`/api/animals/${byTag(t).id}/measurements`, { type: 1, value: kg, measuredOn: day(1) });
await post(`/api/animals/${byTag('2321').id}/breeding-events`, { type: 2, eventDate: day(120) });
await post(`/api/animals/${byTag('2321').id}/breeding-events`, { type: 4, eventDate: day(70), pregnancyResult: 1, gestationDays: 50 });
}

const browser = await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true });
try {
  // ---- Admin portal (manager and HQ) ----
  const admin = await browser.newPage({ viewport: { width: 1366, height: 820 } });
  await admin.goto(`${root}/admin/login`);
  await shot(admin, 'admin-login');
  await admin.getByLabel('Email').fill('admin@felcra.local');
  await admin.getByLabel('Password').fill('Felcra@2026');
  await admin.getByRole('button', { name: 'Sign in' }).click();
  await admin.getByText('Active animals').waitFor();
  await shot(admin, 'admin-dashboard');
  await admin.goto(`${root}/admin/animals`);
  await admin.getByText('A000001').waitFor();
  await shot(admin, 'admin-animals');
  await admin.goto(`${root}/admin/animals/${dam.id}`);
  await admin.getByText('Field records').waitFor();
  await shot(admin, 'admin-animal-detail');
  await admin.goto(`${root}/admin/animals/labels?status=Active`);
  await admin.waitForTimeout(1500);
  await shot(admin, 'admin-qr-labels');
  await admin.goto(`${root}/admin/census`);
  await admin.getByText('Headcount & health programme, July 2026').first().click();
  await admin.getByText('0125').first().waitFor();
  await shot(admin, 'admin-census');
  await admin.getByRole('button', { name: 'Import sheet' }).click();
  await shot(admin, 'admin-census-import');
  await admin.keyboard.press('Escape');
  await admin.goto(`${root}/admin/breeding`);
  await admin.getByRole('heading', { name: 'Herd fertility' }).waitFor();
  await shot(admin, 'admin-breeding');
  await admin.goto(`${root}/admin/analytics/growth`);
  await admin.locator('[data-testid=mature-weight]').first().waitFor();
  await shot(admin, 'admin-growth');
  await admin.goto(`${root}/admin/analytics/ration`);
  await admin.locator('[data-testid=ration-cost]').waitFor();
  await shot(admin, 'admin-ration');
  await admin.goto(`${root}/admin/reports`);
  await admin.getByText('Monthly report e-mail').waitFor();
  await shot(admin, 'admin-reports', { fullPage: true });
  await admin.goto(`${root}/admin/users`);
  await admin.getByText('officer@felcra.local').waitFor();
  await shot(admin, 'admin-users');
  await admin.goto(`${root}/admin/master-data/treatments`);
  await admin.waitForTimeout(1200);
  await shot(admin, 'admin-master-data');
  await admin.goto(`${root}/admin/audit-log`);
  await admin.waitForTimeout(1500);
  await shot(admin, 'admin-audit-log');

  // ---- Field app (phone) ----
  const ctx = await browser.newContext({ viewport: { width: 390, height: 780 }, isMobile: true, hasTouch: true, deviceScaleFactor: 2 });
  const phone = await ctx.newPage();
  await phone.goto(`${root}/field/login`);
  await shot(phone, 'field-login');
  await phone.getByLabel('Email').fill('officer@felcra.local');
  await phone.getByLabel('Password').fill('Felcra@2026');
  await phone.getByRole('button', { name: 'Sign in' }).click();
  await phone.getByText(/140 animals on this phone/).waitFor({ timeout: 30000 });
  await shot(phone, 'field-home');
  await phone.getByRole('link', { name: 'Find by tag number' }).click();
  await phone.getByRole('textbox').first().fill('232');
  await phone.waitForTimeout(800);
  await shot(phone, 'field-find');
  await phone.getByText('Tag 232').first().click();
  await phone.getByText('History', { exact: true }).waitFor();
  await shot(phone, 'field-animal', { fullPage: true });
  await phone.getByRole('link', { name: /Weight/ }).click();
  await phone.getByLabel('Weight (kg)').fill('355');
  await shot(phone, 'field-weight');
  await phone.goBack();
  await phone.getByText('History', { exact: true }).waitFor();
  await phone.getByRole('link', { name: /Treatment/ }).first().click();
  await phone.waitForTimeout(800);
  await shot(phone, 'field-treatment');
  await phone.goBack();
  await phone.getByText('History', { exact: true }).waitFor();
  await phone.getByRole('link', { name: /Breeding/ }).click();
  await phone.waitForTimeout(800);
  await shot(phone, 'field-breeding');
  await phone.evaluate(() => Blazor.navigateTo('sync'));
  await phone.waitForTimeout(1200);
  await shot(phone, 'field-sync', { fullPage: true });
  await phone.evaluate(() => Blazor.navigateTo('ration'));
  await phone.locator('[data-testid=ration-cost]').waitFor();
  await shot(phone, 'field-ration');
  await ctx.setOffline(true);
  await phone.evaluate(() => Blazor.navigateTo(''));
  await phone.waitForTimeout(1200);
  await shot(phone, 'field-offline');
  await ctx.setOffline(false);
} finally {
  await browser.close();
}
