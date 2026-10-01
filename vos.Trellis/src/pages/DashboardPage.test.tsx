import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ConnectionState } from '../types/connection';
import type { RegisteredService } from '../types/mycelium';
import type { EngineMetricsSummary } from '../types/engineMetrics';
import type { VosThing } from '../types/vos';

const stream: { connection: ConnectionState } = { connection: 'live' };
const streamHandlers = new Map<string, ((data?: unknown) => void)[]>();
vi.mock('../hooks/useSse', () => ({
  useSse: () => ({
    connection: stream.connection,
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
vi.mock('../api/configurationApi', () => ({
  configurationApi: { getDefaultPropertyMode: vi.fn(), setDefaultPropertyMode: vi.fn() },
}));

import { myceliumApi } from '../api/myceliumApi';
import { endpointApi } from '../api/endpointApi';
import { engineMetricsApi } from '../api/engineMetricsApi';
import { configurationApi } from '../api/configurationApi';
import { useModelStore } from '../stores/modelStore';
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
    vi.mocked(configurationApi.getDefaultPropertyMode).mockRejectedValue(new Error('no such endpoint'));
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

describe('DashboardPage while the model loads and the connection opens', () => {
  const service: RegisteredService = {
    HandlerId: 'h1', ServiceName: 'consumes', EndpointUrl: 'http://localhost:7102',
    HealthStatus: 'Healthy', RegisteredAt: '', IsRunning: true, FailureCount: 0,
    Stats: { RequestsForwarded: 5, TotalResponseMilliseconds: 50, AverageResponseMilliseconds: 10 },
    IsExternal: false, ConsecutiveFailures: 0,
  };
  const engines: EngineMetricsSummary = {
    ModelId: 'm1', ModelName: 'TestModel',
    Ranges: {
      RegisteredRanges: 12, RangesWithBindings: 3, PropertyDependencyEdges: 20, StateDependencyEdges: 4,
      BindingDependencyEdges: 6, DependencyEdges: 30, EstimatedBytes: 17088,
    },
    Rollups: { ThingsOwningRollups: 2, RollupProperties: 5, MemberEdges: 40, EstimatedBytes: 5120 },
    EstimatedBytesTotal: 22208,
  };
  const thing = (Id: string) => ({ Id, Name: Id, Properties: {} }) as unknown as VosThing;
  const statisticsCard = () => screen.getByText('Model Statistics').parentElement!.parentElement!;
  const figure = (label: string) => within(statisticsCard()).getByText(label).previousSibling?.textContent;

  beforeEach(() => {
    stream.connection = 'live';
    streamHandlers.clear();
    HTMLElement.prototype.scrollTo = () => {};
    vi.mocked(myceliumApi.getServices).mockResolvedValue([service]);
    vi.mocked(endpointApi.getAll).mockResolvedValue([]);
    vi.mocked(engineMetricsApi.getSummary).mockResolvedValue(engines);
    vi.mocked(configurationApi.getDefaultPropertyMode).mockRejectedValue(new Error('no such endpoint'));
    useModelStore.setState({ things: [], relationships: [], loaded: false, holdsWholeModel: false });
  });

  afterEach(() => vi.clearAllMocks());

  // After sign-in the store first holds the few Things the navigation needs, then nothing, then the
  // model. Counted at each step the card read a handful, then zero, then the real figure.
  it('shows no model figure until the store holds the whole model', async () => {
    useModelStore.setState({ things: [thing('a'), thing('b'), thing('c')], relationships: [], loaded: true, holdsWholeModel: false });
    openDashboard();
    await act(async () => {});

    expect(screen.getByText('Reading the model…')).toBeInTheDocument();
    expect(figure('Things')).toBe('—');

    act(() => useModelStore.setState({ holdsWholeModel: true }));
    expect(figure('Things')).toBe('3');
  });

  // A stream that has not opened yet says nothing about the services: the registry's answer stands.
  it('draws a service as the registry reports it while the connection opens, and unreachable once it is lost', async () => {
    stream.connection = 'connecting';
    const { rerender } = openDashboard();
    await act(async () => {});

    expect(screen.getByText('Connecting…')).toBeInTheDocument();
    expect(screen.getByText('Healthy')).toBeInTheDocument();

    stream.connection = 'lost';
    rerender(<MemoryRouter><DashboardPage /></MemoryRouter>);
    expect(screen.getByText('Connection lost')).toBeInTheDocument();
    expect(screen.getByText('Unreachable')).toBeInTheDocument();
    expect(screen.queryByText('Healthy')).toBeNull();
  });

  it('shows the engine figures while the connection opens, and withholds them once it is lost', async () => {
    stream.connection = 'connecting';
    const { rerender } = openDashboard();
    await act(async () => {});

    expect(screen.queryByText('Metrics unavailable')).toBeNull();

    stream.connection = 'lost';
    rerender(<MemoryRouter><DashboardPage /></MemoryRouter>);
    expect(screen.getByText('Metrics unavailable')).toBeInTheDocument();
  });

  // The broker has not failed to answer before it has been asked.
  it('draws Mycelium as connecting until its first answer, then live', async () => {
    let answer: (services: RegisteredService[]) => void = () => {};
    vi.mocked(myceliumApi.getServices).mockReturnValue(new Promise((resolve) => { answer = resolve; }));
    openDashboard();
    await act(async () => {});

    expect(screen.getByRole('img', { name: 'Connecting…' })).toBeInTheDocument();

    await act(async () => { answer([service]); });
    expect(screen.getByRole('img', { name: 'Live' })).toBeInTheDocument();
  });

  it('draws Mycelium as lost when it does not answer', async () => {
    vi.mocked(myceliumApi.getServices).mockRejectedValue(new Error('unreachable'));
    vi.spyOn(console, 'warn').mockImplementation(() => {});
    openDashboard();
    await act(async () => {});

    expect(screen.getByRole('img', { name: 'Connection lost' })).toBeInTheDocument();
  });
});
