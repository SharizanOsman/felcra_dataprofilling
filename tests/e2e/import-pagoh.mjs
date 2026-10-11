// Imports the Pagoh census CSV into the first census session via the API and checks the result.
import fs from 'node:fs';

const base = process.env.BASE ?? 'http://localhost:5237';
const login = await (await fetch(`${base}/api/auth/login`, {
  method: 'POST', headers: { 'content-type': 'application/json' },
  body: JSON.stringify({ email: 'admin@felcra.local', password: 'Felcra@2026' }),
})).json();
const auth = { Authorization: `Bearer ${login.accessToken}` };

let sessions = await (await fetch(`${base}/api/census-sessions`, { headers: auth })).json();
if (sessions.length === 0) {
  const farm = (await (await fetch(`${base}/api/farms`, { headers: auth })).json()).find(f => f.code === 'PAGOH');
  const treatments = (await (await fetch(`${base}/api/treatment-products`, { headers: auth })).json()).map(t => t.id);
  const created = await fetch(`${base}/api/census-sessions`, {
    method: 'POST', headers: { ...auth, 'content-type': 'application/json' },
    body: JSON.stringify({
      farmId: farm.id, title: 'Headcount & health programme, July 2026', startDate: '2026-07-02', endDate: '2026-07-07',
      picName: "Muhamad Alif bin Mohd Sha'ary", picPosition: 'Penolong Eksekutif Veterinar (FFISB)', picPhone: '014-8364377',
      recordedBulls: 11, recordedDams: 61, recordedMaleCalves: 28, recordedFemaleCalves: 40, treatmentProductIds: treatments,
    }),
  });
  console.log('session', created.status);
  sessions = await (await fetch(`${base}/api/census-sessions`, { headers: auth })).json();
}
const sid = sessions[0].id;

const form = new FormData();
form.append('file', new Blob([fs.readFileSync(process.argv[2])], { type: 'text/csv' }), 'pagoh-census-2026-07.csv');
const res = await fetch(`${base}/api/census-sessions/${sid}/import?matchExisting=false`, { method: 'POST', headers: auth, body: form });
console.log('import', res.status, await res.text());

const entries = await (await fetch(`${base}/api/census-sessions/${sid}/entries`, { headers: auth })).json();
const n = entries.map(e => +e.animalNo.slice(1));
console.log('entries', entries.length,
  '| A-numbers follow line order:', n.every((v, i) => i === 0 || v > n[i - 1]),
  '| first', entries[0].animalNo, 'line', entries[0].lineNo,
  '| last', entries.at(-1).animalNo, 'line', entries.at(-1).lineNo,
  '| selected', entries.filter(e => e.selected).length);
const session = await (await fetch(`${base}/api/census-sessions/${sid}`, { headers: auth })).json();
console.log('counted', JSON.stringify(session.counted));
