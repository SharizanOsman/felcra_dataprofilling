// Step 3 end-to-end: import the real Pagoh census through the admin UI, then check animals,
// duplicates, QR labels and the field app's QR landing page. Runs in headless Edge.
import { chromium } from 'playwright-core';

const root = 'http://localhost:5237';
const csvPath = process.argv[2];
const shots = process.argv[3] ?? '.';
const results = [];
const visible = async (locator, timeout = 15000) => { try { await locator.waitFor({ state: 'visible', timeout }); return true; } catch { return false; } };
const check = (name, ok, extra = '') => { results.push({ name, ok }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); };

const browser = await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true });
const consoleErrors = [];

async function login(app, email) {
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(`${email}: ${m.text()}`); });
  page.on('pageerror', e => consoleErrors.push(`${email}: ${e.message}`));
  page.on('response', r => { if (r.status() >= 400) consoleErrors.push(`${email}: HTTP ${r.status()} ${r.request().method()} ${r.url()} (auth header: ${r.request().headers()['authorization'] ? 'yes' : 'no'})`); });
  await page.goto(`${root}/${app}/login`);
  await page.getByLabel('Email').fill(email);
  await page.getByLabel('Password').fill('Felcra@2026');
  await page.getByRole('button', { name: 'Sign in' }).click();
  return { page, context };
}

try {
  const { page: hq, context } = await login('admin', 'admin@felcra.local');
  await hq.getByRole('heading', { name: 'Dashboard' }).waitFor({ timeout: 15000 });

  // ---- New census session -------------------------------------------------------------------
  await hq.getByRole('link', { name: 'Census' }).click();
  await hq.getByRole('button', { name: 'New session' }).click();
  const dlg = hq.getByRole('dialog');
  await dlg.getByLabel('Title').fill('Headcount & health programme, July 2026');
  await dlg.getByLabel('Start date').fill('02 Jul 2026');
  await dlg.getByLabel('End date').fill('07 Jul 2026');
  await dlg.getByLabel('Name').fill('Muhamad Alif bin Mohd Sha\'ary');
  await dlg.getByLabel('Position').fill('Penolong Eksekutif Veterinar (FFISB)');
  await dlg.getByLabel('Bulls (P)').fill('11');
  await dlg.getByLabel('Dams (I)').fill('61');
  await dlg.getByLabel('Male calves (AJ)').fill('28');
  await dlg.getByLabel('Female calves (AB)').fill('40');
  await dlg.getByLabel('Programme treatments').click();
  for (const t of ['Brucella blood screening', 'FMD vaccine (booster)', 'Doramectin (Dectomax)', 'Multivitamin', 'Ear tagging'])
    await hq.getByRole('option', { name: t }).click();
  await hq.keyboard.press('Escape');
  await hq.getByRole('button', { name: 'Save' }).click();
  check('Create census session (navigates to its page)', await visible(hq.getByRole('button', { name: 'Import sheet' })));

  // ---- Import the Pagoh sheet ---------------------------------------------------------------
  await hq.getByRole('button', { name: 'Import sheet' }).click();
  const imp = hq.getByRole('dialog');
  await imp.getByLabel('Match existing animals by tag').uncheck();
  await imp.locator('input[type=file]').setInputFiles(csvPath);
  check('Preview shows 140 new animals', await visible(imp.getByText('New animals: 140')));
  check('Preview totals P 13 · I 60 · AJ 31 · AB 36', await visible(imp.getByText(/P 13 · I 60 · AJ 31 · AB 36 = 140/)));
  check('Preview flags duplicate tags in the file', await visible(imp.getByText(/appears 2 times in this file/).first()));
  check('Preview flags row 17 (3-year-old "male calf")', await visible(imp.getByText(/Recorded as a calf but age is 3 yr/).first()));
  await hq.screenshot({ path: `${shots}/import-preview.png` });
  await imp.getByRole('button', { name: /Import 140 rows/ }).click();
  check('Import completes', await visible(hq.getByText(/Imported: 140 new animal/)));

  // ---- Session summary ----------------------------------------------------------------------
  await hq.waitForTimeout(1000);
  const population = await hq.locator('table').first().innerText();
  check('Counted vs recorded shows the discrepancies', population.includes('13') && population.includes('(+2)') && population.includes('(-4)'),
    population.replace(/\s+/g, ' ').slice(0, 160));
  check('31 animals selected', await visible(hq.getByText('Selected: 31')));
  check('Entries list has 140 animals', await visible(hq.getByText('Animals counted (140)')));
  await hq.screenshot({ path: `${shots}/census-session.png`, fullPage: true });

  // ---- Animals register ---------------------------------------------------------------------
  await hq.getByRole('link', { name: 'Animals' }).click();
  await hq.locator('tbody tr').first().waitFor();
  await hq.getByLabel('Untagged').check();
  check('8 untagged animals', await visible(hq.getByText(/1-8 of 8/)));
  await hq.getByLabel('Untagged').uncheck();
  await hq.getByLabel('Duplicate tags').check();
  check('8 animals share 4 duplicate tags', await visible(hq.getByText(/1-8 of 8/)));
  await hq.screenshot({ path: `${shots}/animals-duplicates.png` });

  // Animal detail with QR code and census history
  await hq.locator('tbody tr', { hasText: '08478' }).first().click();
  check('Animal detail warns about the duplicate tag', await visible(hq.getByText(/is also used by 1 other active animal/)));
  check('Animal detail shows QR code', await visible(hq.locator('.animal-qr svg')));
  check('Animal detail shows census history', await visible(hq.getByRole('link', { name: 'Headcount & health programme, July 2026' })));
  const animalUrl = hq.url();
  const animalId = animalUrl.split('/').pop();

  // Retag to resolve the duplicate
  await hq.getByRole('button', { name: 'Change tag' }).click();
  await hq.getByRole('dialog').getByLabel('New tag number').fill('08478-B');
  await hq.getByRole('dialog').getByLabel('Reason').fill('Duplicate tag corrected');
  await hq.getByRole('button', { name: 'Save' }).click();
  const warningGone = await hq.getByText(/is also used by/).waitFor({ state: 'hidden', timeout: 10000 }).then(() => true, () => false);
  check('Retag resolves the duplicate warning', await visible(hq.getByText('Tag changed.')) && warningGone);
  await hq.screenshot({ path: `${shots}/animal-detail.png`, fullPage: true });

  // Single-animal label
  await hq.getByRole('link', { name: 'Print label' }).click();
  await hq.locator('.label').first().waitFor({ timeout: 15000 });
  check('Print label shows exactly this animal', (await hq.locator('.label').count()) === 1 && await visible(hq.getByText('Tag 08478-B')));

  // Status filter "All" and the full label sheet (via the Animals page buttons)
  await hq.getByRole('link', { name: 'Animals' }).click();
  await hq.locator('tbody tr').first().waitFor();
  await hq.getByLabel('Status').click();
  await hq.getByRole('button', { name: /clear/i }).first().click().catch(() => {});
  await hq.keyboard.press('Escape');
  check('Status filter cleared lists all animals', await visible(hq.getByText(/of 140/)));
  await hq.getByRole('button', { name: 'Print QR labels' }).click();
  await hq.locator('.label').first().waitFor({ timeout: 15000 });
  await hq.waitForTimeout(1000);
  check('Label sheet renders 140 QR labels', (await hq.locator('.label .animal-qr svg').count()) === 140);
  await hq.screenshot({ path: `${shots}/labels.png` });
  await context.close();

  // ---- Field app: QR landing page -------------------------------------------------------------
  const { page: field, context: fieldCtx } = await login('field', 'officer@felcra.local');
  await field.getByText('Hello,').waitFor({ timeout: 15000 });
  await field.goto(`${root}/field/a/${animalId.replaceAll('-', '')}`);
  check('Field app opens the animal from its QR link', await visible(field.getByText('Tag 08478-B')));
  await field.setViewportSize({ width: 400, height: 760 });
  await field.screenshot({ path: `${shots}/field-animal.png` });
  await fieldCtx.close();
} catch (err) {
  check('Unexpected error', false, String(err).split('\n')[0]);
} finally {
  await browser.close();
}

if (consoleErrors.length) console.log('Browser errors:\n  ' + consoleErrors.join('\n  '));
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} checks passed`);
process.exit(failed ? 1 : 0);
