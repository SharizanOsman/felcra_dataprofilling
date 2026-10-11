// Service worker part of task notifications (imported by service-worker.js in development and when published).
self.addEventListener('push', event => {
    let data = {};
    try { data = event.data ? event.data.json() : {}; } catch { data = { body: event.data?.text() }; }
    event.waitUntil(self.registration.showNotification(data.title || 'FELCRA Field', {
        body: data.body || '',
        icon: 'icon-192.png',
        badge: 'icon-192.png',
        tag: data.tag || 'tasks',
        data: { url: data.url || '/field/' },
    }));
});

// Tapping the notification opens the field app (or brings an open one to the front).
self.addEventListener('notificationclick', event => {
    event.notification.close();
    const url = new URL(event.notification.data?.url || '/field/', self.location.origin).href;
    event.waitUntil((async () => {
        const windows = await clients.matchAll({ type: 'window', includeUncontrolled: true });
        const open = windows.find(w => w.url.startsWith(`${self.location.origin}/field/`));
        if (open) {
            await open.focus();
            return open.navigate ? open.navigate(url) : undefined;
        }
        return clients.openWindow(url);
    })());
});
