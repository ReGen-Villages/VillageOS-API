// Each request to the platform is counted as "METHOD /route/with/{id}?parameterNames", so reads of many
// Things count as one route read many times, and a count-only read is told from a full one by its
// parameter. The page's own files are summed as one number, because they are not the platform's traffic.

const IDENTIFIER = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export function routeOf(method, url) {
  const parsed = new URL(url);
  const path = parsed.pathname
    .split('/')
    .map((segment) => (IDENTIFIER.test(segment) ? '{id}' : segment))
    .join('/');
  const names = [...new Set([...parsed.searchParams.keys()])].sort();
  return `${method.toUpperCase()} ${path}${names.length ? '?' + names.join('&') : ''}`;
}

export function isStream(url) {
  return new URL(url).pathname.endsWith('/stream');
}

export function isPlatform(url) {
  return new URL(url).pathname.startsWith('/api/');
}

export class Tally {
  constructor() {
    this.requests = {};
    this.otherRequests = 0;
    this.streamEvents = {};
  }

  request(method, url) {
    if (isStream(url)) return;
    if (!isPlatform(url)) {
      this.otherRequests += 1;
      return;
    }
    const route = routeOf(method, url);
    this.requests[route] = (this.requests[route] ?? 0) + 1;
  }

  streamEvent(kind, count = 1) {
    this.streamEvents[kind] = (this.streamEvents[kind] ?? 0) + count;
  }

  report() {
    const sorted = (counts) =>
      Object.fromEntries(Object.entries(counts).sort(([a], [b]) => a.localeCompare(b)));
    return {
      requests: sorted(this.requests),
      otherRequests: this.otherRequests,
      streamEvents: sorted(this.streamEvents),
    };
  }
}
