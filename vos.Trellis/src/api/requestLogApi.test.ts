import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { apiClient, ApiError } from './client';
import { fetchLatestRequests, fetchRequest, fetchRequestHour, hourOf, mergeNewestFirst, type RequestLogEntry } from './requestLogApi';

function entry(id: string, time: string, connectionId = 'c-gauges'): RequestLogEntry {
  return {
    Id: id, Time: time, ModelId: 'm', ConnectionId: connectionId, ConnectionName: 'reads the gauges',
    Caller: null, SubjectId: null, RelationshipId: null, Status: 200, DurationMilliseconds: 12, BodyBytes: 27, BodyKeptBytes: 0,
  };
}

describe('requestLogApi', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'ensureToken').mockResolvedValue('the-sign-in-token');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('asks for the latest entries, narrowed to a connection where one is named', async () => {
    const get = vi.spyOn(apiClient, 'get').mockResolvedValue([]);

    await fetchLatestRequests();
    await fetchLatestRequests('c-gauges');

    expect(get.mock.calls.map((call) => call[0])).toEqual(['/api/requests', '/api/requests?connection=c-gauges']);
  });

  it('tells an entry found from one no longer kept and one not in the log the reader may read', async () => {
    const found = entry('r1', '2026-10-03T11:15:29+00:00');
    vi.spyOn(apiClient, 'get')
      .mockResolvedValueOnce(found)
      .mockRejectedValueOnce(new ApiError(410, ''))
      .mockRejectedValueOnce(new ApiError(404, ''));

    expect(await fetchRequest('r1')).toEqual({ kind: 'found', entry: found });
    expect(await fetchRequest('r2')).toEqual({ kind: 'noLongerKept' });
    expect(await fetchRequest('r3')).toEqual({ kind: 'notShown' });
  });

  it('passes on any other refusal', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValue(new ApiError(403, ''));

    await expect(fetchRequest('r1')).rejects.toMatchObject({ status: 403 });
  });

  it('downloads an hour under the broker’s file name, and answers nothing for an hour with no file', async () => {
    const fetchStub = vi.fn()
      .mockResolvedValueOnce(new Response('{}\n', {
        status: 200, headers: { 'Content-Disposition': 'attachment; filename="requests-2026100311.jsonl"' },
      }))
      .mockResolvedValueOnce(new Response('', { status: 404 }));
    vi.stubGlobal('fetch', fetchStub);

    const saved = await fetchRequestHour('2026100311');
    const nothing = await fetchRequestHour('2001010100');

    expect(fetchStub.mock.calls[0][0]).toBe('/api/requests/download?hour=2026100311');
    expect(fetchStub.mock.calls[0][1].headers).toEqual({ Authorization: 'Bearer the-sign-in-token' });
    expect(saved?.fileName).toBe('requests-2026100311.jsonl');
    expect(await saved?.blob.text()).toBe('{}\n');
    expect(nothing).toBeNull();
  });

  it('passes on a download the broker refuses for any reason but an hour with no file', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('{"error":"Administrators only"}', { status: 403 })));

    await expect(fetchRequestHour('2026100311')).rejects.toMatchObject({ status: 403 });
  });

  it('orders two entries made in the same instant by their identifiers', () => {
    const first = entry('01a1-a', '2026-10-03T11:00:00+00:00');
    const second = entry('01a1-b', '2026-10-03T11:00:00+00:00');

    expect(mergeNewestFirst([first], [second], 10).map((e) => e.Id)).toEqual(['01a1-b', '01a1-a']);
  });

  it('names an hour the way the broker names its files, in universal time', () => {
    expect(hourOf(new Date('2026-10-03T09:59:59Z'))).toBe('2026100309');
  });

  it('merges entries newest first, once each, up to a limit', () => {
    const older = entry('r1', '2026-10-03T11:00:00.5+00:00');
    const newer = entry('r2', '2026-10-03T11:00:00.25+00:01');
    const newest = entry('r3', '2026-10-03T11:05:00+00:00');

    expect(mergeNewestFirst([older], [newest, older, newer, newest], 10).map((e) => e.Id)).toEqual(['r3', 'r1', 'r2']);
    expect(mergeNewestFirst([older, newer], [newest], 2).map((e) => e.Id)).toEqual(['r3', 'r1']);
  });

  it('puts an entry older than some it holds between them', () => {
    const held = [entry('r3', '2026-10-03T11:05:00+00:00'), entry('r1', '2026-10-03T11:00:00+00:00')];

    expect(mergeNewestFirst(held, [entry('r2', '2026-10-03T11:02:00+00:00')], 10).map((e) => e.Id))
      .toEqual(['r3', 'r2', 'r1']);
  });

  it('hands back the list it holds when nothing arrived is new, so a replay redraws nothing', () => {
    const held = [entry('r2', '2026-10-03T11:01:00+00:00'), entry('r1', '2026-10-03T11:00:00+00:00')];

    expect(mergeNewestFirst(held, [entry('r1', '2026-10-03T11:00:00+00:00')], 10)).toBe(held);
  });
});
