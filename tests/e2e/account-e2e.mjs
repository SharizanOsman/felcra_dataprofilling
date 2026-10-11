// Step 9: change your own password in the admin portal and in the field app (throwaway users).
import { chromium } from 'playwright-core';

const root = process.argv[2] ?? 'http://localhost:5237';
const results = [];
const visible = async (l, timeout = 15000) => { try { await l.waitFor({ state: 'visible', timeout }); return true; } catch { return false; } };
const check = (name, ok, extra = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); };
const errors = [];

const api = async (path, token, body) => fetch(`${root}${path}`, {
  method: body ? 'POST' : 'GET', headers: { 'content-type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
  body: body ? JSON.stringify(body) : undefined });
const tokenFor = async (email, password) => (await (await api('/api/auth/login', null, { email, password })).json()).accessToken;
const hq = await tokenFor('admin@felcra.local', 'Felcra@2026');
const farm = (await (await api('/api/farms', hq)).json())[0];
const stamp = Date.now() % 1000000;
const users = { manager: `pw-manager-${stamp}@felcra.local`, officer: `pw-officer-${stamp}@felcra.local` };
await api('/api/users', hq, { email: users.manager, fullName: 'PW Manager', role: 'FarmManager', farmIds: [farm.id], password: 'Felcra@2026' });
await api('/api/users', hq, { email: users.officer, fullName: 'PW Officer', role: 'FieldOfficer', farmIds: [farm.id], password: 'Felcra@2026' });

const browser = await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true });
const changeIn = async (page, label) => {
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel('Current password').fill('Wrong@2026');
  await dialog.getByLabel('New password').fill('Better@2027');
  await dialog.getByRole('button', { name: 'Save' }).click();
  check(`${label}: wrong current password is refused`, await visible(page.getByText('The current password is not correct.')));
  await dialog.getByLabel('Current password').fill('Felcra@2026');
  await dialog.getByRole('button', { name: 'Save' }).click();
  check(`${label}: password changed, still signed in`, await visible(page.getByText(/Password changed/)));
};
try {
  // Admin portal
  const admin = await browser.newPage({ viewport: { width: 1400, height: 900 } });
  admin.on('pageerror', e => errors.push(e.message));
  await admin.goto(`${root}/admin/login`);
  await admin.getByLabel('Email').fill(users.manager);
  await admin.getByLabel('Password').fill('Felcra@2026');
  await admin.getByRole('button', { name: 'Sign in' }).click();
  await admin.getByRole('button', { name: /PW Manager/ }).click();
  await admin.getByText('Change password').click();
  await changeIn(admin, 'Admin');
  await admin.getByRole('link', { name: 'Animals' }).click();
  check('Admin: keeps working with the new session', await visible(admin.getByRole('heading', { name: 'Animals' })));

  // Field app
  const page = await (await browser.newContext({ viewport: { width: 400, height: 860 }, isMobile: true, hasTouch: true })).newPage();
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(`${root}/field/login`);
  await page.getByLabel('Email').fill(users.officer);
  await page.getByLabel('Password').fill('Felcra@2026');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByText(/animals on this phone/).waitFor({ timeout: 30000 });
  await page.getByRole('link', { name: 'Sync status' }).click();
  await page.getByRole('button', { name: 'Change password' }).click();
  await changeIn(page, 'Field');

  check('Old password no longer signs in', (await api('/api/auth/login', null, { email: users.officer, password: 'Felcra@2026' })).status === 401);
  check('New password signs in', (await api('/api/auth/login', null, { email: users.officer, password: 'Better@2027' })).status === 200);
} catch (e) {
  check('No unexpected exception', false, e.message);
} finally {
  await browser.close();
}
check('No browser errors', errors.length === 0, errors.slice(0, 3).join(' | '));
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
