// Starts Blazor in the language chosen in this browser (Bahasa Melayu unless the user picked English).
// The culture also selects which ICU globalization data is loaded, so it has to be set before start.
(() => {
    let language = null;
    try { language = localStorage.getItem('felcra.language'); } catch { /* storage blocked */ }
    if (language !== 'en' && language !== 'ms') language = 'ms';
    document.documentElement.lang = language;
    Blazor.start({ applicationCulture: language === 'ms' ? 'ms-MY' : 'en-MY' });
})();
