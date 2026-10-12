// Step 8 end-to-end: Reports page downloads (PDF / Excel), census and breeding page shortcuts, monthly e-mail panel.
import { chromium } from 'playwright-core';
import { inEnglish } from './english.mjs';
import fs from 'node:fs';
import path from 'node:path';

const root = process.argv[2] ?? 'http://localhost:5237';
const dir = process.argv[3] ?? '.';
const mailDir = process.argv[4];
const results = [];
const visible = async (l, timeout = 15000) => { try { await l.waitFor({ state: 'visible', timeout }); return true; } catch { return false; } };
const check = (name, ok, extra = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); };
const errors = [];
const today = new Date();
const ym = d => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
const lastMonth = ym(new Date(today.getFullYear(), today.getMonth() - 1, 1));

const browser = inEnglish(await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true }));
const login = async email => {
  const page = await (await browser.newContext({ viewport: { width: 1400, height: 1000 }, acceptDownloads: true })).newPage();
  page.on('console', m => { if (m.type() === 'error' && !/Failed to load resource/.test(m.text())) errors.push(m.text()); });
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(`${root}/admin/login`);
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('Felcra@2026');
  await page.getByRole('button', { name: 'Sign in' }).click();
  await page.getByRole('heading', { name: 'Dashboard' }).waitFor();
  return page;
};
/** Clicks a button and returns the downloaded file's name and first bytes. */
const download = async (page, locator) => {
  const [file] = await Promise.all([page.waitForEvent('download', { timeout: 30000 }), locator.click()]);
  const target = path.join(dir, 'downloads', file.suggestedFilename());
  await file.saveAs(target);
  const bytes = fs.readFileSync(target);
  return { name: file.suggestedFilename(), head: bytes.subarray(0, 4).toString('latin1'), size: bytes.length };
};
fs.mkdirSync(path.join(dir, 'downloads'), { recursive: true });

try {
  // ---- Farm manager -------------------------------------------------------------------------------
  const page = await login('manager@felcra.local');
  await page.getByRole('link', { name: 'Reports' }).click();
  check('Reports page lists four reports', await visible(page.getByText('Farm performance', { exact: true })) && (await page.locator('[data-testid=report-card]').count()) === 4);

  let f = await download(page, page.getByRole('button', { name: 'Farm performance PDF' }));
  check('Farm performance PDF for last month', f.name === `farm-performance-all-farms-${lastMonth}.pdf` && f.head === '%PDF', f.name);
  f = await download(page, page.getByRole('button', { name: 'Health and treatments Excel' }));
  check('Treatments Excel', f.name === `treatments-all-farms-${lastMonth}.xlsx` && f.head === 'PK\u0003\u0004', f.name);

  await page.getByText('This month', { exact: true }).click();
  f = await download(page, page.getByRole('button', { name: 'Farm performance Excel' }));
  check('Period preset changes the report period', f.name.startsWith(`farm-performance-all-farms-${ym(today)}-01-to-`), f.name);

  await page.getByRole('combobox', { name: 'Farm' }).click();
  await page.getByRole('option', { name: /PAGOH/ }).click();
  f = await download(page, page.getByRole('button', { name: 'Culling candidates PDF' }));
  check('Farm filter applies to the file', f.name.startsWith('culling-candidates-pagoh-'), f.name);

  f = await download(page, page.getByRole('button', { name: 'Census and profiling form PDF' }));
  check('Census and profiling form PDF', f.name === 'census-form-pagoh-2026-07-02.pdf' && f.head === '%PDF' && f.size > 20000, `${f.name} ${f.size} bytes`);
  f = await download(page, page.getByRole('button', { name: 'Selected animals Excel' }));
  check('Selected animals Excel', f.name === 'selected-animals-pagoh-2026-07-02.xlsx', f.name);
  check('Monthly e-mail panel is HQ only', !(await page.getByText('Monthly report e-mail').isVisible()));
  await page.screenshot({ path: path.join(dir, 'reports-page.png'), fullPage: true });

  // Shortcuts on the census programme and breeding pages
  await page.getByRole('link', { name: 'Census' }).click();
  await page.getByText('Headcount & health programme, July 2026').first().click();
  await page.getByRole('button', { name: 'Reports' }).click();
  f = await download(page, page.getByText('Census sheet (Excel)'));
  check('Census page exports the census sheet', f.name === 'census-form-pagoh-2026-07-02.xlsx', f.name);
  await page.getByRole('link', { name: 'Breeding' }).click();
  f = await download(page, page.getByRole('button', { name: 'Culling candidates Excel' }));
  check('Breeding page exports culling candidates', f.name.startsWith('culling-candidates-all-farms-') && f.name.endsWith('.xlsx'), f.name);
  await page.context().close();

  // ---- HQ: monthly e-mail ------------------------------------------------------------------------
  const hq = await login('admin@felcra.local');
  await hq.getByRole('link', { name: 'Reports' }).click();
  check('Monthly e-mail panel shows status', await visible(hq.getByText('Monthly report e-mail')) && await visible(hq.getByText('E-mail configured')));
  const before = mailDir ? fs.readdirSync(mailDir).length : 0;
  await hq.getByRole('button', { name: 'Send me a test e-mail' }).click();
  check('Test e-mail sent', await visible(hq.getByText('Test e-mail sent to admin@felcra.local with 3 reports.')));
  if (mailDir) check('Test e-mail written to the pickup folder', fs.readdirSync(mailDir).length === before + 1);
  await hq.screenshot({ path: path.join(dir, 'reports-hq.png'), fullPage: true });
} catch (e) {
  check('No unexpected exception', false, e.message);
} finally {
  await browser.close();
}

check('No browser errors', errors.length === 0, errors.slice(0, 3).join(' | '));
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} passed`);
process.exit(failed ? 1 : 0);
