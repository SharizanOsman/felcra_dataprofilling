// The apps start in Bahasa Melayu unless the browser has chosen English. The suites check English text, so every
// context they open starts with English chosen (the same localStorage value the language switch saves).
const chooseEnglish = () => { try { localStorage.setItem('felcra.language', 'en'); } catch { /* storage blocked */ } };

/** Wraps a Browser (new contexts and pages start in English) or a BrowserContext (made English now). */
export function inEnglish(target) {
  if (typeof target.addInitScript === 'function') {
    target.addInitScript(chooseEnglish);
    return target;
  }
  const newContext = target.newContext.bind(target);
  target.newContext = async (options) => {
    const context = await newContext(options);
    await context.addInitScript(chooseEnglish);
    return context;
  };
  target.newPage = async (options) => (await target.newContext(options)).newPage();
  return target;
}
