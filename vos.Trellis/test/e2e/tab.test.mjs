import { afterEach, describe, expect, it } from 'vitest';
import { countStreamEvents, parse } from './tab.mjs';

describe('the arguments', () => {
  it('has a default for everything and reads seconds as a number', () => {
    expect(parse([])).toEqual({
      url: 'http://localhost:5173', page: '/', model: null, seconds: 60, until: null, out: null, headful: false,
    });
    expect(parse(['--seconds', '15', '--page', '/things']).seconds).toBe(15);
    expect(parse(['--seconds', '15', '--page', '/things']).page).toBe('/things');
  });

  it('takes headful as a flag with no value', () => {
    expect(parse(['--headful', '--model', 'Site']).headful).toBe(true);
    expect(parse(['--headful', '--model', 'Site']).model).toBe('Site');
  });

  it('refuses an argument it does not know', () => {
    expect(() => parse(['--speed', '3'])).toThrow('unknown argument: --speed');
  });
});

describe('counting the stream events the console handles', () => {
  const original = globalThis.EventSource;
  afterEach(() => {
    globalThis.EventSource = original;
    delete window.__streamEvents;
  });

  it('counts an event under the kind the console registered for, and still hands it on', () => {
    class FakeEventSource {
      constructor() { this.listeners = {}; }
      addEventListener(kind, listener) { this.listeners[kind] = listener; }
    }
    globalThis.EventSource = FakeEventSource;
    countStreamEvents();

    const handled = [];
    const source = new FakeEventSource();
    source.addEventListener('ThingCreated', (event) => handled.push(event.data));
    source.addEventListener('StatesChanged', () => handled.push('states'));
    source.listeners.ThingCreated({ data: 'a' });
    source.listeners.ThingCreated({ data: 'b' });
    source.listeners.StatesChanged({});

    expect(window.__streamEvents).toEqual({ ThingCreated: 2, StatesChanged: 1 });
    expect(handled).toEqual(['a', 'b', 'states']);
  });
});
