// Web Push for task notifications: browser side of subscribing this device.
(() => {
    const supported = () => 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;

    function keyBytes(base64Url) {
        const base64 = (base64Url + '='.repeat((4 - base64Url.length % 4) % 4)).replace(/-/g, '+').replace(/_/g, '/');
        return Uint8Array.from(atob(base64), c => c.charCodeAt(0));
    }

    const current = async () => (await navigator.serviceWorker.ready).pushManager.getSubscription();

    window.felcraPush = {
        /** 'unsupported' | 'denied' | 'on' | 'off' */
        async status() {
            if (!supported()) return 'unsupported';
            if (Notification.permission === 'denied') return 'denied';
            return (await current()) ? 'on' : 'off';
        },

        /** Asks permission and subscribes; returns { endpoint, p256dh, auth }, or null if permission was refused. */
        async subscribe(publicKey) {
            if (await Notification.requestPermission() !== 'granted') return null;
            const registration = await navigator.serviceWorker.ready;
            let subscription = await registration.pushManager.getSubscription();
            // A subscription made with an older server key cannot be reused.
            const key = keyBytes(publicKey);
            const existingKey = subscription?.options?.applicationServerKey;
            if (subscription && existingKey && new Uint8Array(existingKey).join() !== key.join()) {
                await subscription.unsubscribe();
                subscription = null;
            }
            subscription ??= await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: key });
            const json = subscription.toJSON();
            return { endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth };
        },

        /** Unsubscribes this device; returns the old endpoint (to remove on the server) or null. */
        async unsubscribe() {
            if (!supported()) return null;
            const subscription = await current();
            if (!subscription) return null;
            const endpoint = subscription.endpoint;
            await subscription.unsubscribe();
            return endpoint;
        },
    };
})();
