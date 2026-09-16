import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';

const streamHandlers = new Map<string, ((data?: unknown) => void)[]>();
vi.mock('../hooks/useSse', () => ({
  useSse: () => ({
    connected: true,
    on: (event: string, handler: (data?: unknown) => void) => {
      streamHandlers.set(event, [...(streamHandlers.get(event) ?? []), handler]);
      return () => streamHandlers.set(event, (streamHandlers.get(event) ?? []).filter((held) => held !== handler));
    },
  }),
  useSubscription: () => {},
}));
vi.mock('../api/myceliumApi', () => ({
  myceliumApi: { getServices: vi.fn(), startService: vi.fn(), stopService: vi.fn(), shutdown: vi.fn(), reloadSeeds: vi.fn() },
}));
vi.mock('../api/endpointApi', () => ({ endpointApi: { getAll: vi.fn() } }));
vi.mock('../api/engineMetricsApi', () => ({ engineMetricsApi: { getSummary: vi.fn() } }));
vi.mock('../api/configApi', () => ({
  configApi: { getDefaultPropertyMode: vi.fn(), setDefaultPropertyMode: vi.fn() },
}));

import { myceliumApi } from '../api/myceliumApi';
import { endpointApi } from '../api/endpointApi';
import { engineMetricsApi } from '../api/engineMetricsApi';
import { configApi } from '../api/configApi';
import { DashboardPage } from './DashboardPage';

/** Longer than the page's refresh window, so a burst meant to cost one read has had every chance to
 *  cost more. */
const WELL_PAST_ONE_WINDOW = 10_000;

function arrived(event: string, times: number): void {
  for (let count = 0; count < times; count += 1) streamHandlers.get(event)?.forEach((handler) => handler({}));
}

function openDashboard() {
  return render(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>,
  );
}

async function settled(): Promise<void> {
  await act(async () => { await vi.advanceTimersByTimeAsync(0); });
}

const servicesReads = () => vi.mocked(myceliumApi.getServices).mock.calls.length;
const endpointReads = () => vi.mocked(endpointApi.getAll).mock.calls.length;

describe('DashboardPage registry refresh', () => {
  beforeEach(() => {
    streamHandlers.clear();
    // jsdom lays nothing out and scrolls nothing; the feed scrolls to its newest line on mount.
    HTMLElement.prototype.scrollTo = () => {};
    vi.mocked(myceliumApi.getServices).mockResolvedValue([]);
    vi.mocked(endpointApi.getAll).mockResolvedValue([]);
    vi.mocked(engineMetricsApi.getSummary).mockRejectedValue(new Error('no engine'));
    vi.mocked(configApi.getDefaultPropertyMode).mockRejectedValue(new Error('no such endpoint'));
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('reads each registry once for a burst of completed service requests', async () => {
    openDashboard();
    await settled();
    const servicesBefore = servicesReads();
    const endpointsBefore = endpointReads();

    await act(async () => {
      arrived('ServiceRequestCompleted', 200);
      arrived('EndpointServiceRequestCompleted', 200);
      await vi.advanceTimersByTimeAsync(WELL_PAST_ONE_WINDOW);
    });

    expect(servicesReads()).toBe(servicesBefore + 1);
    expect(endpointReads()).toBe(endpointsBefore + 1);
  });

  it('keeps reading the registry while the events keep coming', async () => {
    openDashboard();
    await settled();
    const servicesBefore = servicesReads();

    await act(async () => {
      arrived('ServiceRequestCompleted', 200);
      await vi.advanceTimersByTimeAsync(WELL_PAST_ONE_WINDOW);
      arrived('ServiceRequestCompleted', 200);
      await vi.advanceTimersByTimeAsync(WELL_PAST_ONE_WINDOW);
    });

    expect(servicesReads()).toBe(servicesBefore + 2);
  });

  it('reads the registry again for a health or daemon change, within the same window', async () => {
    openDashboard();
    await settled();
    const servicesBefore = servicesReads();

    await act(async () => {
      arrived('ServiceHealthChanged', 1);
      arrived('DaemonStatusChanged', 1);
      await vi.advanceTimersByTimeAsync(WELL_PAST_ONE_WINDOW);
    });

    expect(servicesReads()).toBe(servicesBefore + 1);
  });

  it('does not read after the page is left', async () => {
    const { unmount } = openDashboard();
    await settled();
    const servicesBefore = servicesReads();

    await act(async () => { arrived('ServiceRequestCompleted', 1); });
    unmount();
    await act(async () => { await vi.advanceTimersByTimeAsync(WELL_PAST_ONE_WINDOW); });

    expect(servicesReads()).toBe(servicesBefore);
  });
});
