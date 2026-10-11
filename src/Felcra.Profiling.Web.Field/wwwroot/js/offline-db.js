// Minimal IndexedDB key/value store for the field app's offline data. Values are JSON strings
// produced by .NET, so the browser side stays generic.
window.felcraDb = (() => {
    const NAME = 'felcra-field';
    const VERSION = 1;
    const STORES = ['animals', 'records', 'outbox', 'photos', 'meta'];
    let dbPromise = null;

    function open() {
        dbPromise ??= new Promise((resolve, reject) => {
            const request = indexedDB.open(NAME, VERSION);
            request.onupgradeneeded = () => {
                for (const store of STORES) {
                    if (!request.result.objectStoreNames.contains(store)) request.result.createObjectStore(store);
                }
            };
            request.onsuccess = () => resolve(request.result);
            request.onerror = () => reject(request.error);
        });
        return dbPromise;
    }

    // Runs work(store) in one transaction and resolves when the transaction has committed.
    async function run(storeName, mode, work) {
        const db = await open();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(storeName, mode);
            let result;
            tx.oncomplete = () => resolve(result);
            tx.onerror = () => reject(tx.error);
            tx.onabort = () => reject(tx.error);
            const request = work(tx.objectStore(storeName));
            if (request) request.onsuccess = () => { result = request.result; };
        });
    }

    return {
        get: (store, key) => run(store, 'readonly', s => s.get(key)).then(v => v ?? null),
        getAll: store => run(store, 'readonly', s => s.getAll()),
        getAllKeys: store => run(store, 'readonly', s => s.getAllKeys()),
        count: store => run(store, 'readonly', s => s.count()),
        put: (store, key, value) => run(store, 'readwrite', s => s.put(value, key)),
        delete: (store, key) => run(store, 'readwrite', s => s.delete(key)),
        clear: store => run(store, 'readwrite', s => s.clear()),

        /** Writes many [key, value] pairs and deletes keys in a single transaction. */
        bulk: (store, puts, deletes) => run(store, 'readwrite', s => {
            for (const [key, value] of puts) s.put(value, key);
            for (const key of deletes) s.delete(key);
            return null;
        }),

        /** Removes all offline data (on sign-out). */
        clearAll: async () => {
            for (const store of STORES) await run(store, 'readwrite', s => s.clear());
        },
    };
})();

window.felcraNet = {
    isOnline: () => navigator.onLine,

    /** Calls dotnetRef.OnConnectivityChanged(bool) when the browser goes online or offline. */
    subscribe(dotnetRef) {
        window.addEventListener('online', () => dotnetRef.invokeMethodAsync('OnConnectivityChanged', true));
        window.addEventListener('offline', () => dotnetRef.invokeMethodAsync('OnConnectivityChanged', false));
    },
};
