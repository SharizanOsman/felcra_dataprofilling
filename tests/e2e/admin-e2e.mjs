// End-to-end check of the Step 2 admin portal in real Edge (headless) via playwright-core.
import { chromium } from 'playwright-core';

const base = 'http://localhost:5237/admin/';
const shots = process.argv[2] ?? '.';
const suffix = Date.now().toString().slice(-5);
const results = [];
const visible = async (locator) => { try { await locator.waitFor({ state: 'visible', timeout: 10000 }); return true; } catch { return false; } };
const check = (name, ok, extra = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); };

const browser = await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true });
const consoleErrors = [];

async function login(email, expectOk = true) {
  const context = await browser.newContext({ viewport: { width: 1366, height: 860 } });
  const page = await context.newPage();
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(`${email}: ${m.text()}`); });
  await page.goto(base + 'login');
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('Felcra@2026');
  await page.getByRole('button', { name: 'Sign in' }).click();
  if (expectOk) await page.getByRole('heading', { name: 'Dashboard' }).waitFor({ timeout: 15000 });
  return { page, context };
}

async function save(page, snackbarText) {
  await page.getByRole('button', { name: 'Save' }).click();
  await page.getByText(snackbarText).first().waitFor({ timeout: 10000 });
}

async function rowWith(page, text) {
  return page.locator('tr', { hasText: text }).first();
}

try {
  // ---------------- HQ admin ----------------
  const { page: hq, context: hqCtx } = await login('admin@felcra.local');
  check('HQ admin signs in and sees dashboard', true);
  check('HQ nav shows Users and Audit log', await hq.getByRole('link', { name: 'Users' }).isVisible() && await hq.getByRole('link', { name: 'Audit log' }).isVisible());

  // Farms: list, add, edit
  await hq.getByRole('link', { name: 'Farms' }).click();
  await hq.getByText('PAGOH').first().waitFor();
  check('Farms list shows seeded PAGOH with its projects', await (await rowWith(hq, 'PAGOH')).innerText().then(t => t.includes('EPP5') && t.includes('RP4')));

  const farmCode = `T${suffix}`;
  await hq.getByRole('button', { name: 'Add farm' }).click();
  await hq.getByRole('dialog').waitFor();
  await hq.getByRole('dialog').getByLabel('Code').fill(farmCode);
  await hq.getByRole('dialog').getByLabel('Name', { exact: true }).fill('UI test farm');
  await hq.getByRole('dialog').getByLabel('State').fill('Pahang');
  await save(hq, 'Farm added.');
  await hq.locator('td', { hasText: farmCode }).first().waitFor({ timeout: 10000 }).catch(() => {});
  check('Add farm via dialog', await hq.locator('td', { hasText: farmCode }).first().isVisible());

  await (await rowWith(hq, farmCode)).getByRole('button', { name: 'Edit' }).click();
  await hq.getByRole('dialog').getByLabel('Name', { exact: true }).fill('UI test farm (edited)');
  await save(hq, 'Farm updated.');
  check('Edit farm via dialog', await hq.getByText('UI test farm (edited)').first().isVisible());

  // Client-side validation blocks a bad farm code
  await hq.getByRole('button', { name: 'Add farm' }).click();
  await hq.getByRole('dialog').getByLabel('Code').fill('bad code!');
  await hq.getByRole('dialog').getByLabel('Name', { exact: true }).fill('x');
  await hq.getByRole('button', { name: 'Save' }).click();
  const codeError = hq.getByRole('dialog').getByText('Code may only contain');
  await codeError.waitFor({ timeout: 5000 }).catch(() => {});
  check('Client validation shows field error and keeps dialog open', await codeError.isVisible());
  await hq.getByRole('button', { name: 'Cancel' }).click();

  // Server conflict (duplicate code) is surfaced in the dialog
  await hq.getByRole('button', { name: 'Add farm' }).click();
  await hq.getByRole('dialog').getByLabel('Code').fill('PAGOH');
  await hq.getByRole('dialog').getByLabel('Name', { exact: true }).fill('Duplicate');
  await hq.getByRole('button', { name: 'Save' }).click();
  const dup = hq.getByRole('dialog').getByText('already exists');
  await dup.waitFor({ timeout: 5000 }).catch(() => {});
  check('Duplicate code conflict shown in dialog', await dup.isVisible());
  await hq.getByRole('button', { name: 'Cancel' }).click();

  // Farm detail + herd groups
  await hq.getByRole('link', { name: 'FELCRA Berhad Kawasan Pagoh' }).click();
  await hq.getByText('Herd groups / pens').waitFor();
  await hq.getByRole('button', { name: 'Add group' }).click();
  await hq.getByRole('dialog').getByLabel('Name', { exact: true }).fill(`Pen ${suffix}`);
  await save(hq, 'Group added.');
  check('Add herd group on farm detail', await visible(hq.getByText(`Pen ${suffix}`).first()));
  await hq.screenshot({ path: `${shots}/farm-detail.png`, fullPage: true });

  // Master data pages
  await hq.getByRole('link', { name: 'Projects & schemes' }).click();
  await hq.getByText('Entry Point Project 5').waitFor();
  check('Projects page lists EPP5', true);

  await hq.getByRole('link', { name: 'Breeds' }).click();
  await hq.getByText('Brahman Cross').waitFor();
  check('Breeds page lists Brahman Cross', true);

  await hq.getByRole('link', { name: 'Treatments & vaccines' }).click();
  await hq.getByText('Doramectin (Dectomax)').waitFor();
  check('Treatments page shows humanised category', await hq.getByText('Diagnostic test').first().isVisible());

  await hq.getByRole('link', { name: 'Feed ingredients' }).click();
  await hq.getByRole('button', { name: 'Add ingredient' }).click();
  await hq.getByRole('dialog').getByLabel('Name', { exact: true }).fill(`Napier grass ${suffix}`);
  await hq.getByRole('dialog').getByLabel('Dry matter %').fill('150');
  await hq.getByRole('button', { name: 'Save' }).click();
  const dmError = hq.getByRole('dialog').getByText(/must be between 0 and 100/);
  await dmError.waitFor({ timeout: 5000 }).catch(() => {});
  check('Out-of-range dry matter is rejected with a message (not clamped)', await dmError.isVisible() && await hq.getByRole('dialog').isVisible());
  await hq.getByRole('dialog').getByLabel('Dry matter %').fill('20');
  await hq.getByRole('dialog').getByLabel('Crude protein %').fill('8.5');
  await hq.getByRole('dialog').getByLabel('Price (RM per kg as fed)').fill('0.12');
  await save(hq, 'Ingredient added.');
  check('Add feed ingredient with nutrients', await visible(hq.getByText(`Napier grass ${suffix}`).first()));
  await hq.screenshot({ path: `${shots}/feeds.png`, fullPage: true });

  // Users
  await hq.getByRole('link', { name: 'Users' }).click();
  await hq.getByText('manager@felcra.local').waitFor();
  const newEmail = `officer${suffix}@felcra.local`;
  await hq.getByRole('button', { name: 'Add user' }).click();
  const dlg = hq.getByRole('dialog');
  await dlg.getByLabel('Full name').fill('UI Test Officer');
  await dlg.getByLabel('Email').fill(newEmail);
  await dlg.getByLabel('Farms').click();
  await hq.getByRole('option', { name: /PAGOH/ }).click();
  await hq.keyboard.press('Escape');
  await dlg.getByLabel('Initial password').fill('Officer@2026');
  await save(hq, 'User added.');
  check('Add field officer with farm assignment', await (await rowWith(hq, newEmail)).innerText().then(t => t.includes('Field officer') && t.includes('PAGOH')));
  await hq.screenshot({ path: `${shots}/users.png`, fullPage: true });

  // Audit log
  await hq.getByRole('link', { name: 'Audit log' }).click();
  await hq.getByText('Created').first().waitFor();
  const auditText = await hq.locator('table').innerText();
  check('Audit log records farm, group, ingredient and user changes',
    ['Farm', 'HerdGroup', 'FeedIngredient', 'User'].every(t => auditText.includes(t)));
  await hq.screenshot({ path: `${shots}/audit.png`, fullPage: true });

  // No page may show an error banner after loading.
  const pages = ['farms', 'master-data/projects', 'master-data/breeds', 'master-data/treatments', 'master-data/feeds', 'users', 'audit-log'];
  const pagesWithErrors = [];
  for (const p of pages) {
    await hq.goto(base + p);
    await hq.locator('h4').first().waitFor();
    await hq.waitForTimeout(1500);
    if (await hq.locator('.mud-alert-text-error, .mud-alert-filled-error, .mud-alert-outlined-error').count() > 0) pagesWithErrors.push(p);
  }
  check('No admin page shows an error banner', pagesWithErrors.length === 0, pagesWithErrors.join(', '));
  await hqCtx.close();

  // ---------------- Farm manager ----------------
  const { page: mgr, context: mgrCtx } = await login('manager@felcra.local');
  check('Manager nav hides Users', !(await mgr.getByRole('link', { name: 'Users' }).isVisible()));
  await mgr.getByRole('link', { name: 'Farms' }).click();
  await mgr.getByText('PAGOH').first().waitFor();
  check('Manager sees only own farm and no Add farm button',
    !(await visible(mgr.getByText(farmCode).first())) && !(await mgr.getByRole('button', { name: 'Add farm' }).isVisible()));
  await mgr.getByRole('link', { name: 'FELCRA Berhad Kawasan Pagoh' }).click();
  await mgr.getByText('Herd groups / pens').waitFor({ timeout: 10000 });
  await mgr.getByRole('button', { name: 'Add group' }).click();
  await mgr.getByRole('dialog').getByLabel('Name', { exact: true }).fill(`Mgr pen ${suffix}`);
  await save(mgr, 'Group added.');
  check('Manager can add herd groups on own farm', await visible(mgr.getByText(`Mgr pen ${suffix}`).first()));
  await mgr.goto(base + 'users');
  await mgr.getByText('You do not have permission').waitFor({ timeout: 10000 }).catch(() => {});
  check('Manager is blocked from the Users page', await visible(mgr.getByText('You do not have permission').first()));
  await mgrCtx.close();

  // ---------------- Field officer ----------------
  const { page: off, context: offCtx } = await login('officer@felcra.local', false);
  await off.getByText('does not have access').waitFor({ timeout: 10000 }).catch(() => {});
  check('Field officer cannot sign in to the admin portal', await off.getByText('does not have access').isVisible());
  await offCtx.close();
} catch (err) {
  check('Unexpected error', false, String(err).split('\n')[0]);
} finally {
  await browser.close();
}

if (consoleErrors.length) console.log('Browser console errors:\n  ' + consoleErrors.join('\n  '));
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} checks passed`);
process.exit(failed ? 1 : 0);
