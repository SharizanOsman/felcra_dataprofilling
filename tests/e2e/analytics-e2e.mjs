// Step 7 end-to-end: admin dashboard, growth, lactation and ration pages; field ration planner (incl. offline).
import { chromium } from 'playwright-core';
import path from 'node:path';

const root = process.argv[2] ?? 'http://localhost:5237';
const dir = process.argv[3] ?? '.';
const results = [];
const visible = async (l, timeout = 15000) => { try { await l.waitFor({ state: 'visible', timeout }); return true; } catch { return false; } };
const check = (name, ok, extra = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); };
const errors = [];
const money = async l => Number((await l.innerText()).replace(/[^0-9.]/g, ''));

const browser = await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true });
try {
  // ---- Admin --------------------------------------------------------------------------------------
  const admin = await browser.newPage({ viewport: { width: 1400, height: 1000 } });
  admin.on('console', m => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) errors.push(m.text()); });
  admin.on('pageerror', e => errors.push(e.message));
  await admin.goto(`${root}/admin/login`);
  await admin.getByLabel('Email').fill('manager@felcra.local');
  await admin.getByLabel('Password').fill('Felcra@2026');
  await admin.getByRole('button', { name: 'Sign in' }).click();

  const kpi = label => admin.locator('[data-testid=kpi]').filter({ hasText: label }).locator('[data-testid=kpi-value]');
  check('Dashboard shows 140 active animals', await visible(kpi('Active animals')) && (await kpi('Active animals').innerText()) === '140');
  check('Dashboard shows capacity and tag problems', await visible(admin.getByText('70% of capacity (200)'))
    && await visible(admin.getByText('8 untagged · 8 sharing a tag')));
  check('Pregnancy rate is blank until breeding is recorded', (await kpi('Pregnancy rate').innerText()) === '–'
    && await visible(admin.getByText('60 breeding females have no records')));
  check('Herd by category table', await visible(admin.getByRole('cell', { name: 'Dam (I)' })) && await visible(admin.getByRole('cell', { name: '307 kg' })));
  check('Activity chart rendered', await visible(admin.locator('svg[aria-label="Records per week"]')));
  await admin.screenshot({ path: path.join(dir, 'dashboard.png'), fullPage: true });

  // Growth
  await admin.getByRole('link', { name: 'Growth' }).click();
  const mature = admin.locator('[data-testid=mature-weight]').first();
  check('Female growth curve fitted', await visible(mature, 20000), await mature.innerText().catch(() => ''));
  check('Growth chart with curve and points', await visible(admin.locator('svg[aria-label="Growth curve"] polyline').first())
    && (await admin.locator('svg[aria-label="Growth curve"] circle').count()) > 50);
  await admin.getByRole('tab', { name: 'Males', exact: true }).click();
  check('Male curve tab', await visible(admin.locator('[data-testid=mature-weight]').last()));
  const belowRows = admin.locator('table').last().locator('tbody tr');
  check('Below-curve list has animals', (await belowRows.count()) > 5, `${await belowRows.count()} rows`);
  await admin.screenshot({ path: path.join(dir, 'growth.png'), fullPage: true });
  await belowRows.first().click();
  check('Below-curve row opens the animal', await visible(admin.getByText('Field records')));

  // Lactation (beef herd: no milk yet)
  await admin.getByRole('link', { name: 'Lactation' }).click();
  check('Lactation explains there is no milk data', await visible(admin.locator('[data-testid=lactation-message]').filter({ hasText: 'No milk yields recorded yet' })));

  // Ration planner
  await admin.getByRole('link', { name: 'Ration planner' }).click();
  const cost = admin.locator('[data-testid=ration-cost]');
  check('Ration formulated from seeded feeds', await visible(cost), await cost.innerText().catch(() => ''));
  const maintenance = await money(cost);
  await admin.getByRole('combobox', { name: 'Animal class' }).click();
  await admin.getByRole('option', { name: /Finishing/ }).click();
  await admin.waitForTimeout(300);
  const finishing = await money(cost);
  check('Finishing ration costs more than maintenance', finishing > maintenance, `RM ${maintenance} → RM ${finishing}`);
  check('Nutrient table shows TDN target', await visible(admin.getByRole('cell', { name: '≥ 70' })));
  await admin.screenshot({ path: path.join(dir, 'ration-admin.png'), fullPage: true });
  // Leave only the grasses: the finishing energy target cannot be met.
  for (const name of ['Palm kernel cake (PKC)', 'Rice bran', 'Soybean meal', 'Molasses']) await admin.getByLabel(name, { exact: true }).click();
  check('Infeasible targets are explained', await visible(admin.getByText(/no ingredient reaches the TDN target of 70/)));
  await admin.close();

  // ---- Field (phone) -------------------------------------------------------------------------------
  const context = await browser.newContext({ viewport: { width: 400, height: 860 }, isMobile: true, hasTouch: true });
  const page = await context.newPage();
  page.on('console', m => { if (m.type() === 'error' && !/Failed to load resource|ERR_INTERNET_DISCONNECTED/.test(m.text())) errors.push(m.text()); });
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(`${root}/field/login`);
  await page.getByLabel('Email').fill('officer@felcra.local');
  await page.getByLabel('Password').fill('Felcra@2026');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByText(/140 animals on this phone/).waitFor({ timeout: 30000 });

  await context.setOffline(true);
  await page.getByRole('link', { name: 'Ration planner' }).click();
  const fieldCost = page.locator('[data-testid=ration-cost]');
  check('Field ration works offline from synced feeds', await visible(fieldCost), await fieldCost.innerText().catch(() => ''));
  const one = await money(fieldCost);
  const heads = page.getByLabel('Number of animals');
  await heads.fill('20');
  await heads.press('Tab');
  check('Group total for 20 animals', await visible(page.getByText('20 animals per day')) && await visible(page.getByText(`RM ${(one * 20).toFixed(2)}`)));
  await page.screenshot({ path: path.join(dir, 'ration-field.png'), fullPage: true });
  await context.setOffline(false);
  await context.close();
} catch (e) {
  check('No unexpected exception', false, e.message);
} finally {
  await browser.close();
}

check('No browser errors', errors.length === 0, errors.slice(0, 3).join(' | '));
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
