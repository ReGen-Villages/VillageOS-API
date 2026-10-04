import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import type { RequestLogEntry, RequestLookup } from '../api/requestLogApi';
import { useModelStore } from '../stores/modelStore';
import { ARCHETYPE_FLAG } from '../pipeline/model';
import type { ConnectionState } from '../types/connection';

vi.mock('../hooks/useSse', () => ({ useSubscription: () => {} }));

const log: { entries: RequestLogEntry[]; streamState: ConnectionState; askedFor: (string | undefined)[] } =
  { entries: [], streamState: 'live', askedFor: [] };
vi.mock('../hooks/useRequestLog', () => ({
  useRequestLog: (connection: string | undefined) => {
    log.askedFor.push(connection);
    return { entries: log.entries, streamState: log.streamState };
  },
}));

const lookup: { answer: RequestLookup } = { answer: { kind: 'notShown' } };
const hour: { file: { blob: Blob; fileName: string } | null } = { file: null };
vi.mock('../api/requestLogApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/requestLogApi')>()),
  fetchRequest: vi.fn(async () => lookup.answer),
  fetchRequestHour: vi.fn(async () => hour.file),
}));

vi.mock('../utils/logDownload', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../utils/logDownload')>()),
  triggerDownload: vi.fn(),
}));

import { RequestsPage } from './RequestsPage';
import { fetchRequest, fetchRequestHour, hourOf } from '../api/requestLogApi';
import { triggerDownload } from '../utils/logDownload';

function entry(id: string, overrides: Partial<RequestLogEntry> = {}): RequestLogEntry {
  return {
    Id: id, Time: '2026-10-03T11:15:29+00:00', ModelId: 'm', ConnectionId: 'c-gauges', ConnectionName: 'reads the gauges',
    Caller: 'mara', SubjectId: null, RelationshipId: null, Status: 200, DurationMilliseconds: 12, BodyBytes: 27, BodyKeptBytes: 0,
    ...overrides,
  };
}

function holdConnections() {
  const marked = { [ARCHETYPE_FLAG.Connection]: true };
  useModelStore.setState({
    things: [
      { Id: 'is', Name: 'is', Properties: {} },
      { Id: 'connection-kind', Name: 'Connection', IsArchetype: true, Properties: marked },
      { Id: 'c-gauges', Name: 'reads the gauges', Properties: {} },
      { Id: 'c-reservoir', Name: 'fills the reservoir', Properties: {} },
    ],
    relationships: [
      { Id: 'r1', Name: '', SubjectId: 'c-gauges', PredicateId: 'is', TargetId: 'connection-kind', Properties: {} },
      { Id: 'r2', Name: '', SubjectId: 'c-reservoir', PredicateId: 'is', TargetId: 'connection-kind', Properties: {} },
    ],
    loaded: true,
  } as never);
}

function Address() {
  const location = useLocation();
  return <output data-testid="address">{location.pathname + location.search}</output>;
}

function renderAt(address: string) {
  render(
    <MemoryRouter initialEntries={[address]}>
      <Routes>
        <Route path="/requests" element={<><RequestsPage /><Address /></>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('RequestsPage', () => {
  beforeEach(() => {
    log.entries = [];
    log.streamState = 'live';
    log.askedFor = [];
    lookup.answer = { kind: 'notShown' };
    hour.file = null;
    useModelStore.setState({ things: [], relationships: [], loaded: true } as never);
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('lists each entry with its time, status, duration, connection and caller', () => {
    log.entries = [entry('r2', { Status: 0, DurationMilliseconds: 30000 }), entry('r1')];

    renderAt('/requests');

    const rows = screen.getAllByRole('row').slice(1);
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('nothing answered');
    expect(rows[0]).toHaveTextContent('30000 ms');
    expect(rows[1]).toHaveTextContent('2026-10-03 11:15:29');
    expect(rows[1]).toHaveTextContent('200');
    expect(rows[1]).toHaveTextContent('reads the gauges');
    expect(rows[1]).toHaveTextContent('mara');
  });

  it('says so while the last day holds no requests', () => {
    renderAt('/requests');

    expect(screen.getByText('No requests in the last day yet.')).toBeInTheDocument();
  });

  it('offers the model’s connections and narrows the log to the one chosen', () => {
    holdConnections();
    renderAt('/requests');

    const choice = screen.getByRole('combobox', { name: 'Connection' });
    expect([...(choice as HTMLSelectElement).options].map((o) => o.textContent))
      .toEqual(['Every connection', 'fills the reservoir', 'reads the gauges']);

    fireEvent.change(choice, { target: { value: 'c-gauges' } });

    expect(screen.getByTestId('address')).toHaveTextContent('/requests?connection=c-gauges');
    expect(log.askedFor.at(-1)).toBe('c-gauges');
  });

  it('opens an entry and says it is no longer kept once past the log’s retention period', async () => {
    lookup.answer = { kind: 'noLongerKept' };

    renderAt('/requests?entry=r9');

    expect(await screen.findByText(/no longer kept/)).toBeInTheDocument();
  });

  it('opens an entry and says when none is in the log the reader may read', async () => {
    renderAt('/requests?entry=r9');

    expect(await screen.findByText('No such request is in the log you may read.')).toBeInTheDocument();
  });

  it('opens an entry in full', async () => {
    lookup.answer = { kind: 'found', entry: entry('r1', { SubjectId: 's-north', BodyKeptBytes: 27 }) };

    renderAt('/requests?entry=r1');

    const panel = await screen.findByRole('region', { name: 'Request' });
    expect(panel).toHaveTextContent('r1');
    expect(panel).toHaveTextContent('s-north');
    expect(panel).toHaveTextContent('27 bytes, 27 kept');
  });

  it('closes an open entry, keeping the connection chosen', async () => {
    lookup.answer = { kind: 'found', entry: entry('r1') };
    renderAt('/requests?connection=c-gauges&entry=r1');

    fireEvent.click(await screen.findByRole('button', { name: 'Close' }));

    expect(screen.getByTestId('address')).toHaveTextContent('/requests?connection=c-gauges');
  });

  it.each([
    ['connecting', 'Connecting…'],
    ['live', 'Streaming'],
    ['lost', 'Reconnecting'],
  ] as const)('says a %s request stream in words that fit it', (streamState, words) => {
    log.streamState = streamState;

    renderAt('/requests');

    expect(screen.getByText(words)).toBeInTheDocument();
  });

  it('says so when the hour asked for holds no requests', async () => {
    renderAt('/requests');

    fireEvent.click(screen.getByRole('button', { name: 'Download hour' }));

    await waitFor(() => expect(fetchRequestHour).toHaveBeenCalled());
    expect(await screen.findByText('No requests were recorded in that hour.')).toBeInTheDocument();
  });

  it('asks for the hour chosen as the broker names it, and saves the file under the broker’s name', async () => {
    hour.file = { blob: new Blob(['{}\n']), fileName: 'requests-2026100311.jsonl' };
    renderAt('/requests');

    fireEvent.change(screen.getByLabelText('Hour'), { target: { value: '2026-10-03T13:00' } });
    fireEvent.click(screen.getByRole('button', { name: 'Download hour' }));

    await waitFor(() => expect(triggerDownload).toHaveBeenCalledWith(hour.file!.blob, 'requests-2026100311.jsonl'));
    expect(fetchRequestHour).toHaveBeenCalledWith(hourOf(new Date('2026-10-03T13:00')));
  });

  it('says so when the hour cannot be downloaded', async () => {
    vi.mocked(fetchRequestHour).mockRejectedValueOnce(new Error('refused'));
    renderAt('/requests');

    fireEvent.click(screen.getByRole('button', { name: 'Download hour' }));

    expect(await screen.findByText('Could not download that hour.')).toBeInTheDocument();
  });

  it('opens a request from its identifier in the list, keeping the connection chosen', async () => {
    log.entries = [entry('r1')];
    lookup.answer = { kind: 'found', entry: entry('r1') };
    renderAt('/requests?connection=c-gauges');

    fireEvent.click(screen.getByRole('button', { name: 'r1' }));

    expect(screen.getByTestId('address')).toHaveTextContent('/requests?connection=c-gauges&entry=r1');
    expect(await screen.findByRole('region', { name: 'Request' })).toHaveTextContent('reads the gauges (c-gauges)');
  });

  it('widens the log to every connection again', () => {
    holdConnections();
    renderAt('/requests?connection=c-gauges');

    fireEvent.change(screen.getByRole('combobox', { name: 'Connection' }), { target: { value: '' } });

    expect(screen.getByTestId('address')).toHaveTextContent(/^\/requests$/);
    expect(log.askedFor.at(-1)).toBeUndefined();
  });

  it('shows nothing from a request read after its panel was closed', async () => {
    let answer: (lookup: RequestLookup) => void = () => {};
    vi.mocked(fetchRequest).mockReturnValueOnce(new Promise((resolve) => { answer = resolve; }));
    renderAt('/requests?entry=r1');

    fireEvent.click(screen.getByRole('button', { name: 'Close' }));
    answer({ kind: 'noLongerKept' });

    await waitFor(() => expect(screen.queryByRole('region', { name: 'Request' })).toBeNull());
    expect(screen.queryByText(/no longer kept/)).toBeNull();
  });

  it('says so when a request cannot be read', async () => {
    vi.mocked(fetchRequest).mockRejectedValueOnce(new Error('refused'));

    renderAt('/requests?entry=r1');

    expect(await screen.findByText('Could not read the request.')).toBeInTheDocument();
  });
});
