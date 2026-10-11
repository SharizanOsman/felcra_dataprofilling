// Builds src/Felcra.Profiling.Web.Admin/wwwroot/data/malaysia-states.json from Natural Earth's 1:10m
// admin-1 boundaries (public domain): https://github.com/nvkelso/natural-earth-vector
//   node tools/build-malaysia-map.mjs <ne_10m_admin_1_states_provinces.geojson>
// East Malaysia is drawn closer to the peninsula (the sea gap shortened), as on most maps of Malaysia;
// MalaysiaMap.razor places farm pins with the same projection, stored in the output.
import fs from 'node:fs';

const source = process.argv[2];
const out = 'src/Felcra.Profiling.Web.Admin/wwwroot/data/malaysia-states.json';
const east = new Set(['Sabah', 'Sarawak', 'Labuan']);
const projection = { lonMin: 99.5, latMax: 7.5, eastShift: 4.2, eastFromLon: 108.5, scale: 47.5, pad: 12 };
const width = 760;

const x = (lon, shifted) => projection.pad + (lon - projection.lonMin - (shifted ? projection.eastShift : 0)) * projection.scale;
const y = lat => projection.pad + (projection.latMax - lat) * projection.scale;

// Douglas-Peucker on screen points.
function simplify(points, tolerance) {
  if (points.length < 4) return points;
  const keep = new Uint8Array(points.length);
  keep[0] = keep[points.length - 1] = 1;
  const stack = [[0, points.length - 1]];
  while (stack.length) {
    const [a, b] = stack.pop();
    const [ax, ay] = points[a], [bx, by] = points[b];
    const len = Math.hypot(bx - ax, by - ay) || 1;
    let worst = -1, index = -1;
    for (let i = a + 1; i < b; i++) {
      const d = Math.abs((bx - ax) * (ay - points[i][1]) - (ax - points[i][0]) * (by - ay)) / len;
      if (d > worst) { worst = d; index = i; }
    }
    if (worst > tolerance) { keep[index] = 1; stack.push([a, index], [index, b]); }
  }
  return points.filter((_, i) => keep[i]);
}

// A closed ring starts and ends on the same point: simplify its two halves, split at the middle.
const simplifyRing = (ring, tolerance) => {
  const mid = Math.floor(ring.length / 2);
  return [...simplify(ring.slice(0, mid + 1), tolerance).slice(0, -1), ...simplify(ring.slice(mid), tolerance)];
};

const geo = JSON.parse(fs.readFileSync(source, 'utf8'));
let maxY = 0;
const states = geo.features
  .filter(f => f.properties.adm0_a3 === 'MYS')
  .map(f => {
    const shifted = east.has(f.properties.name);
    const polygons = f.geometry.type === 'Polygon' ? [f.geometry.coordinates] : f.geometry.coordinates;
    const rings = polygons.map(p => p[0])
      .map(ring => simplifyRing(ring.map(([lon, lat]) => [x(lon, shifted), y(lat)]), 0.6))
      .filter(ring => {
        const xs = ring.map(p => p[0]), ys = ring.map(p => p[1]);
        return ring.length >= 4 && (Math.max(...xs) - Math.min(...xs)) + (Math.max(...ys) - Math.min(...ys)) > 3; // drop specks
      });
    rings.flat().forEach(([, py]) => { maxY = Math.max(maxY, py); });
    const path = rings.map(r => 'M' + r.map(([px, py]) => `${px.toFixed(1)} ${py.toFixed(1)}`).join('L') + 'Z').join('');
    return { name: f.properties.name, type: f.properties.type_en, east: shifted, path };
  })
  .sort((a, b) => a.name.localeCompare(b.name));

const result = {
  source: 'Natural Earth 1:10m admin-1 states and provinces (public domain), simplified; East Malaysia moved closer',
  viewBox: [width, Math.ceil(maxY + projection.pad)],
  projection,
  states,
};
fs.mkdirSync('src/Felcra.Profiling.Web.Admin/wwwroot/data', { recursive: true });
fs.writeFileSync(out, JSON.stringify(result));
console.log(`${states.length} states, ${fs.statSync(out).size} bytes, viewBox ${result.viewBox.join('x')}`);
