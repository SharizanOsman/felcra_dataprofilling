// Bahasa Melayu / English in both apps, and the farm map: place and GPS search, picking a location in the farm form.
// Place search goes through the API to OpenStreetMap, so this suite needs internet.
import { chromium } from 'playwright-core';
import fs from 'node:fs';

const base = (process.argv[2] ?? 'http://localhost:5237').replace(/\/?$/, '/');
const shots = process.argv[3] ?? 'out';
fs.mkdirSync(shots, { recursive: true });
const results = [];
const check = (name, ok, extra = '') => { results.push(ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${extra ? '  ' + extra : ''}`); };
const visible = async (l, t = 15000) => { try { await l.waitFor({ state: 'visible', timeout: t }); return true; } catch { return false; } };

const browser = await chromium.launch({ channel: process.env.E2E_CHANNEL ?? 'msedge', headless: true });
const context = await browser.newContext({ viewport: { width: 1366, height: 900 }, geolocation: { latitude: 2.1483, longitude: 102.7712 }, permissions: ['geolocation'] });
const page = await context.newPage();
const errors = [];
page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
page.on('pageerror', e => errors.push(e.message));

try {
  // Default language is Malay.
  await page.goto(base + 'admin/login');
  check('Login page is in Malay by default', await visible(page.getByRole('button', { name: 'Log masuk' })));
  check('html lang is ms', (await page.getAttribute('html', 'lang')) === 'ms');
  await page.getByLabel('E-mel').fill('admin@felcra.local');
  await page.getByLabel('Kata laluan').fill('Felcra@2026');
  await page.getByRole('button', { name: 'Log masuk' }).click();
  check('Dashboard heading in Malay', await visible(page.getByRole('heading', { name: 'Papan pemuka' })));
  check('Nav shows Ladang', await page.getByRole('link', { name: 'Ladang' }).isVisible());
  await page.screenshot({ path: `${shots}/1-dashboard-ms.png` });

  // Farms map.
  await page.getByRole('link', { name: 'Ladang' }).click();
  check('Farms page in Malay', await visible(page.getByRole('heading', { name: 'Ladang' })));
  check('Leaflet map renders', await visible(page.locator('[data-testid=farm-map-canvas] .leaflet-tile-loaded').first(), 20000));
  check('PAGOH pin label on the map', await visible(page.locator('.farm-map-label', { hasText: 'PAGOH' })));
  await page.screenshot({ path: `${shots}/2-farms-map-ms.png` });

  // Search a place name (through the server to OpenStreetMap).
  const search = page.locator('input[data-testid=place-search], [data-testid=place-search] input').first();
  await search.fill('Kluang');
  await search.press('Enter');
  const placeShown = await visible(page.locator('[data-testid=place-result]').first(), 20000);
  check('Place search returns results', placeShown, placeShown ? await page.locator('[data-testid=place-result]').first().innerText() : '');
  if (placeShown) {
    await page.locator('[data-testid=place-result]').first().click();
    check('Chosen place is shown with Add farm here', await visible(page.locator('[data-testid=map-place]')) && await page.getByRole('button', { name: 'Tambah ladang di sini' }).isVisible());
  }
  // GPS coordinates.
  await search.fill('2.148, 102.771');
  await search.press('Enter');
  check('GPS search returns the point', await visible(page.locator('[data-testid=place-result]').first(), 20000),
    await page.locator('[data-testid=place-result]').first().innerText().catch(() => ''));
  await page.locator('[data-testid=place-result]').first().click();
  await page.waitForTimeout(1500);
  await page.screenshot({ path: `${shots}/3-gps-search.png` });

  // Farm form: location picker.
  await page.getByRole('button', { name: 'Tambah ladang di sini' }).click();
  await page.getByRole('dialog').waitFor();
  check('Picker map in the farm form', await visible(page.locator('[data-testid=location-picker-map] .leaflet-tile-loaded').first(), 20000));
  const lat = await page.getByRole('dialog').getByLabel('Latitud').inputValue();
  check('Found place pre-fills latitude', lat.startsWith('2.148'), lat);
  const map = page.locator('[data-testid=location-picker-map]');
  const box = await map.boundingBox();
  await page.mouse.click(box.x + box.width * 0.3, box.y + box.height * 0.4);
  await page.waitForTimeout(800);
  const lat2 = await page.getByRole('dialog').getByLabel('Latitud').inputValue();
  check('Clicking the map moves the location', lat2 !== lat && lat2.length > 0, lat2);
  check('State filled from the place', (await page.getByRole('dialog').locator('input').evaluateAll(i => i.map(x => x.value))).includes('Johor'));
  await page.getByRole('dialog').getByRole('button', { name: 'Guna lokasi saya' }).click();
  await page.waitForTimeout(1500);
  check('Use my location fills GPS', (await page.getByRole('dialog').getByLabel('Latitud').inputValue()).startsWith('2.1483'));
  await page.screenshot({ path: `${shots}/4-picker.png` });
  // Malay validation message in the form.
  await page.getByRole('dialog').getByRole('button', { name: 'Simpan' }).click();
  check('Validation message in Malay', await visible(page.getByText("'Kod' wajib diisi.").first(), 5000));
  await page.getByRole('dialog').getByRole('button', { name: 'Batal' }).click();

  // Switch to English.
  await page.getByTestId('language-menu').getByRole('button').first().click();
  await page.locator('.mud-popover-open').getByText('English', { exact: true }).click();
  check('After switching: English page', await visible(page.getByRole('heading', { name: 'Farms' }), 30000));
  check('html lang is en', (await page.getAttribute('html', 'lang')) === 'en');
  await page.screenshot({ path: `${shots}/5-farms-en.png` });

  // Field app follows the same choice; switch back to Malay there.
  const field = await context.newPage();
  await field.goto(base + 'field/login');
  check('Field app uses the chosen English', await visible(field.getByRole('button', { name: 'Sign in' }), 30000));
  await field.getByTestId('language-menu').getByRole('button').first().click();
  await field.locator('.mud-popover-open').getByText('Bahasa Melayu', { exact: true }).click();
  check('Field app back in Malay', await visible(field.getByRole('button', { name: 'Log masuk' }), 30000));
  await field.getByLabel('E-mel').fill('officer@felcra.local');
  await field.getByLabel('Kata laluan').fill('Felcra@2026');
  await field.getByRole('button', { name: 'Log masuk' }).click();
  check('Field home in Malay', await visible(field.getByRole('button', { name: 'Imbas tag telinga' }).or(field.getByText('Imbas tag telinga')).first(), 30000));
  await field.setViewportSize({ width: 400, height: 820 });
  await field.screenshot({ path: `${shots}/6-field-ms.png` });
} catch (e) {
  check('Unexpected error', false, e.message);
} finally {
  const csp = errors.filter(e => /Content Security Policy|Refused/.test(e));
  check('No CSP violations', csp.length === 0, csp.join(' | '));
  console.log('Console errors:', errors.length ? errors.slice(0, 8).join('\n') : 'none');
  console.log(`\n${results.filter(Boolean).length}/${results.length} checks passed`);
  await browser.close();
}
process.exit(results.every(Boolean) ? 0 : 1);
