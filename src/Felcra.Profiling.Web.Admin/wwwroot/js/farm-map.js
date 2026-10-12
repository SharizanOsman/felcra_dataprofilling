// Leaflet map of the farms (OpenStreetMap / Esri satellite tiles), used by FarmMap and LocationPicker.
// Text shown in popups is set with textContent, never as HTML: farm names are user input.

const MALAYSIA = { center: [4.2, 108.0], zoom: 5 };

/**
 * Creates a map in `element`. `dotnet` receives OnFarmClicked(id) and, in picker mode, OnPicked(lat, lon).
 * options: { picker: bool, labels: { street, satellite, openFarm } }
 */
export function createMap(element, dotnet, options) {
    const labels = options.labels ?? {};
    const map = L.map(element, { zoomControl: true, worldCopyJump: false }).setView(MALAYSIA.center, MALAYSIA.zoom);

    const street = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
        maxZoom: 19,
        attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a>',
    }).addTo(map);
    const satellite = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
        maxZoom: 19,
        attribution: 'Tiles &copy; Esri &mdash; Esri, Maxar, Earthstar Geographics',
    });
    L.control.layers({ [labels.street ?? 'Street']: street, [labels.satellite ?? 'Satellite']: satellite }, null, { position: 'topright' }).addTo(map);
    L.control.scale({ imperial: false }).addTo(map);

    const farmLayer = L.featureGroup().addTo(map);
    let place = null;
    let pin = null;

    const pinIcon = L.divIcon({ className: 'farm-map-pin', html: '<span></span>', iconSize: [28, 28], iconAnchor: [14, 28] });
    const placeIcon = L.divIcon({ className: 'farm-map-place', html: '<span></span>', iconSize: [22, 22], iconAnchor: [11, 22] });

    const popup = (title, lines, link) => {
        const box = document.createElement('div');
        const strong = document.createElement('strong');
        strong.textContent = title;
        box.appendChild(strong);
        for (const line of lines.filter(Boolean)) {
            const div = document.createElement('div');
            div.textContent = line;
            box.appendChild(div);
        }
        if (link) {
            const a = document.createElement('a');
            a.href = '#';
            a.textContent = link.text;
            a.addEventListener('click', e => { e.preventDefault(); link.onClick(); });
            box.appendChild(a);
        }
        return box;
    };

    const setPin = (lat, lon, pan) => {
        if (!pin) {
            pin = L.marker([lat, lon], { icon: pinIcon, draggable: true, autoPan: true, keyboard: true }).addTo(map);
            pin.on('dragend', () => {
                const p = pin.getLatLng();
                dotnet.invokeMethodAsync('OnPicked', p.lat, p.lng);
            });
        } else {
            pin.setLatLng([lat, lon]);
        }
        if (pan) map.setView([lat, lon], Math.max(map.getZoom(), 15));
    };

    if (options.picker) {
        map.getContainer().style.cursor = 'crosshair';
        map.on('click', e => {
            setPin(e.latlng.lat, e.latlng.lng, false);
            dotnet.invokeMethodAsync('OnPicked', e.latlng.lat, e.latlng.lng);
        });
    }

    // Leaflet measures its box once; maps in dialogs or tabs open at size 0, so measure again when it changes.
    const resize = new ResizeObserver(() => map.invalidateSize());
    resize.observe(element);

    return {
        /** farms: [{ id, code, name, lat, lon, radius, active, lines, link }] (link: false hides "Open farm") */
        setFarms(farms) {
            farmLayer.clearLayers();
            for (const f of farms) {
                const marker = L.circleMarker([f.lat, f.lon], {
                    radius: f.radius, weight: 2, color: '#ffffff',
                    fillColor: '#2E7D32', fillOpacity: f.active ? 0.85 : 0.35,
                });
                marker.bindTooltip(f.code, { permanent: true, direction: 'right', offset: [f.radius, 0], className: 'farm-map-label' });
                marker.bindPopup(() => popup(`${f.name} (${f.code})`, f.lines, f.link === false ? null
                    : { text: labels.openFarm ?? 'Open farm', onClick: () => dotnet.invokeMethodAsync('OnFarmClicked', f.id) }));
                marker.farmId = f.id;
                marker.addTo(farmLayer);
            }
        },
        fitFarms() {
            const bounds = farmLayer.getBounds();
            if (bounds.isValid()) map.fitBounds(bounds.pad(0.3), { maxZoom: 13 });
            else map.setView(MALAYSIA.center, MALAYSIA.zoom);
        },
        openFarm(id) {
            farmLayer.eachLayer(m => {
                if (m.farmId === id) {
                    map.setView(m.getLatLng(), Math.max(map.getZoom(), 13));
                    m.openPopup();
                }
            });
        },
        /** Marks a search result (not a farm) and centres on it. */
        showPlace(lat, lon, title, lines, zoom) {
            if (place) place.remove();
            place = L.marker([lat, lon], { icon: placeIcon }).addTo(map).bindPopup(popup(title, lines ?? []));
            map.setView([lat, lon], zoom ?? 14);
            place.openPopup();
        },
        clearPlace() {
            if (place) place.remove();
            place = null;
        },
        setPin(lat, lon, pan) { setPin(lat, lon, pan); },
        clearPin() {
            if (pin) pin.remove();
            pin = null;
        },
        flyTo(lat, lon, zoom) { map.setView([lat, lon], zoom); },
        dispose() {
            resize.disconnect();
            map.remove();
        },
    };
}

/** The device's position: { lat, lon, accuracy } (metres). Rejects with the browser's message if refused or unavailable. */
export function locate() {
    return new Promise((resolve, reject) => {
        if (!navigator.geolocation) return reject(new Error('unsupported'));
        navigator.geolocation.getCurrentPosition(
            p => resolve({ lat: p.coords.latitude, lon: p.coords.longitude, accuracy: p.coords.accuracy }),
            e => reject(new Error(e.code === 1 ? 'denied' : 'unavailable')),
            { enableHighAccuracy: true, timeout: 15000, maximumAge: 60000 });
    });
}
