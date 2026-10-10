// Camera QR scanning for ear-tag labels, wrapping the vendored html5-qrcode library.
window.felcraScanner = {
    instance: null,

    /** Starts the rear camera in the given element; calls dotnetRef.OnScanned(text) once per code. */
    async start(elementId, dotnetRef) {
        await this.stop();
        const lib = window.__Html5QrcodeLibrary__;
        this.instance = new lib.Html5Qrcode(elementId, {
            formatsToSupport: [lib.Html5QrcodeSupportedFormats.QR_CODE],
            verbose: false,
        });
        let last = null;
        await this.instance.start(
            { facingMode: 'environment' },
            {
                fps: 10,
                qrbox: (w, h) => {
                    const size = Math.floor(Math.min(w, h) * 0.7);
                    return { width: size, height: size };
                },
            },
            text => {
                if (text === last) return; // the camera sees the same code many times a second
                last = text;
                dotnetRef.invokeMethodAsync('OnScanned', text);
            },
            () => { /* no code in this frame */ });
    },

    async stop() {
        if (!this.instance) return;
        try {
            await this.instance.stop();
        } catch {
            // not running
        }
        try {
            this.instance.clear();
        } catch {
            // already cleared
        }
        this.instance = null;
    },
};
