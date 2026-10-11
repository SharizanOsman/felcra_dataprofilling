// Backup / restore drill helper: "before" records data, "after" adds a change, "check" verifies the restore.
// NODE_TLS_REJECT_UNAUTHORIZED=0 node restore-drill.mjs <before|after|check> [state.json]
import fs from 'node:fs';
const root = 'https://localhost:8443';
const [, , step, stateFile = 'restore-drill.json'] = process.argv;
const login = async () => ({ Authorization: `Bearer ${(await (await fetch(`${root}/api/auth/login`, { method: 'POST',
  headers: { 'content-type': 'application/json' }, body: JSON.stringify({ email: 'admin@felcra.local', password: 'Felcra@2026' }) })).json()).accessToken}` });
const h = await login();
const json = { ...h, 'content-type': 'application/json' };
const animals = async q => (await (await fetch(`${root}/api/animals?pageSize=200${q ?? ''}`, { headers: h })).json());
// Smallest valid JPEG (1x1 px).
const jpeg = Buffer.from('/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=', 'base64');

if (step === 'before') {
  const a = (await animals('&search=232')).items.find(x => x.tagNumber === '232');
  const m = await fetch(`${root}/api/animals/${a.id}/measurements`, { method: 'POST', headers: json, body: JSON.stringify({ type: 1, value: 351 }) });
  const form = new FormData();
  form.append('file', new Blob([jpeg], { type: 'image/jpeg' }), 'cow.jpg');
  const p = await (await fetch(`${root}/api/animals/${a.id}/photos`, { method: 'POST', headers: h, body: form })).json();
  const total = (await animals()).totalCount;
  fs.writeFileSync(stateFile, JSON.stringify({ animalId: a.id, photoId: p.id, total }));
  console.log('before: measurement', m.status, 'photo', p.id, 'animals', total);
} else if (step === 'after') {
  const farm = (await (await fetch(`${root}/api/farms`, { headers: h })).json())[0];
  const r = await fetch(`${root}/api/animals`, { method: 'POST', headers: json, body: JSON.stringify({ farmId: farm.id, tagNumber: 'AFTER-BACKUP', category: 2, sex: 2 }) });
  console.log('after: added animal', r.status, 'animals', (await animals()).totalCount);
} else {
  const s = JSON.parse(fs.readFileSync(stateFile, 'utf8'));
  const total = (await animals()).totalCount;
  const after = (await animals('&search=AFTER-BACKUP')).items.length;
  const photo = await fetch(`${root}/api/photos/${s.photoId}`, { headers: h });
  const records = await (await fetch(`${root}/api/animals/${s.animalId}/records`, { headers: h })).json();
  const weight = records.measurements.some(m => m.value === 351);
  const ok = total === s.total && after === 0 && photo.status === 200 && weight;
  console.log(`check: animals ${total} (expected ${s.total}), later animal present ${after}, photo ${photo.status}, weight kept ${weight} => ${ok ? 'RESTORE OK' : 'RESTORE FAILED'}`);
  process.exit(ok ? 0 : 1);
}
