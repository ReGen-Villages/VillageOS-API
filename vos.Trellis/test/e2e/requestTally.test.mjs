import { describe, expect, it } from 'vitest';
import { Tally, isStream, routeOf } from './requestTally.mjs';

describe('a route with its identifiers taken out', () => {
  it('replaces every identifier segment and keeps the parameter names, sorted', () => {
    expect(
      routeOf('get', 'http://127.0.0.1:5099/api/states/3aa33815-8382-4288-a7b6-177bbc3715c1/things?countOnly=true&b=1&a=2'),
    ).toBe('GET /api/states/{id}/things?a&b&countOnly');
  });

  it('leaves a path with no identifier and no query as it is', () => {
    expect(routeOf('GET', 'http://localhost/api/services')).toBe('GET /api/services');
  });
});

describe('the tally', () => {
  it('counts a route once per request, whatever the identifier', () => {
    const tally = new Tally();
    tally.request('GET', 'http://h/api/things/3aa33815-8382-4288-a7b6-177bbc3715c1');
    tally.request('GET', 'http://h/api/things/20feb542-03ee-4d73-9220-bed2da758ca7');
    tally.request('GET', 'http://h/api/services');
    expect(tally.report().requests).toEqual({ 'GET /api/services': 1, 'GET /api/things/{id}': 2 });
  });

  it('leaves the streams themselves out, because one open stream is not a request rate', () => {
    const tally = new Tally();
    tally.request('GET', 'http://h/api/subscriptions/3aa33815-8382-4288-a7b6-177bbc3715c1/stream?access_token=x');
    tally.request('GET', 'http://h/api/events/stream?access_token=x');
    expect(tally.report().requests).toEqual({});
    expect(isStream('http://h/api/events/stream?x=1')).toBe(true);
  });

  it('counts handled stream events by kind', () => {
    const tally = new Tally();
    tally.streamEvent('ThingCreated');
    tally.streamEvent('ThingCreated');
    tally.streamEvent('StatesChanged');
    expect(tally.report().streamEvents).toEqual({ StatesChanged: 1, ThingCreated: 2 });
  });
});

describe('what is not the platform', () => {
  it('sums the page assets and fonts as one number instead of listing them', () => {
    const tally = new Tally();
    tally.request('GET', 'http://h/assets/index-B4UnkLQu.js');
    tally.request('GET', 'https://fonts.gstatic.com/s/schibstedgrotesk/v7/x.woff2');
    tally.request('GET', 'http://h/api/things');
    expect(tally.report()).toEqual({
      requests: { 'GET /api/things': 1 },
      otherRequests: 2,
      streamEvents: {},
    });
  });
});

describe('a count of events handled', () => {
  it('is added by kind rather than replayed one at a time', () => {
    const tally = new Tally();
    tally.streamEvent('ThingCreated', 3);
    tally.streamEvent('ThingCreated');
    expect(tally.report().streamEvents).toEqual({ ThingCreated: 4 });
  });
});
