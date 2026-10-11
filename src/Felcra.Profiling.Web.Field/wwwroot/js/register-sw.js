// Registered from a file (not inline) so the Content-Security-Policy can forbid inline scripts.
navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });
