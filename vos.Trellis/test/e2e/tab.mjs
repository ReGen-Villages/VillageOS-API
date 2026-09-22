// The counting tab's two decisions that run without a browser: what its arguments mean, and how a
// stream event is counted. Kept out of the script so a test can import them without launching it.

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

// A named stream event never reaches `onmessage`, so the count is taken where the console registers
// for each kind: every listener it adds to an EventSource is wrapped to count under that kind before
// handing the event on. Self-contained, because the browser receives it as source text and runs it
// before the page's own scripts — the streams open on sign-in.
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
