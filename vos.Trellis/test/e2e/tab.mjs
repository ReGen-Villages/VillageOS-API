// Kept apart from the counting script so a test can import these without starting a browser.

export function parse(argv) {
  const values = { url: 'http://localhost:5173', page: '/', model: null, seconds: 60, until: null, out: null, headful: false };
  for (let i = 0; i < argv.length; i++) {
    const name = argv[i].replace(/^--/, '');
    if (name === 'headful') values.headful = true;
    else if (name in values) values[name] = argv[++i];
    else throw new Error(`unknown argument: ${argv[i]}`);
  }
  values.seconds = Number(values.seconds);
  return values;
}

// A named stream event never reaches `onmessage`, so each listener is wrapped where it is added. It uses
// nothing from outside itself, because the browser runs its source text before the page's own scripts.
export function countStreamEvents() {
  window.__streamEvents = {};
  const register = EventSource.prototype.addEventListener;
  EventSource.prototype.addEventListener = function (kind, listener, options) {
    const counted = function (event) {
      window.__streamEvents[kind] = (window.__streamEvents[kind] ?? 0) + 1;
      return listener.call(this, event);
    };
    return register.call(this, kind, counted, options);
  };
}
