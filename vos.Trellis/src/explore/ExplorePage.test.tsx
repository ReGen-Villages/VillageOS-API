import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MULTI_STEP_FORM_TEST_TIMEOUT_MILLISECONDS } from '../testTimeouts';

vi.setConfig({ testTimeout: MULTI_STEP_FORM_TEST_TIMEOUT_MILLISECONDS });

vi.mock('../api/intakeApi', () => ({
  intakeApi: {
    configured: vi.fn(),
    formOptions: vi.fn(),
    parcelAt: vi.fn(),
    searchPlaces: vi.fn(),
    askForCode: vi.fn(),
    exchangeTicket: vi.fn(),
    submitWithTicket: vi.fn(),
  },
}));
vi.mock('../api/findingsApi', () => ({ findingsApi: { readWithTicket: vi.fn() } }));
// The map cannot build in a test's document, and what this page owes it is only the wiring: the pick
// handler when no position exists, and the boundary when one does.
vi.mock('../components/map/MapView', () => ({
  MapView: (props: { onPositionPick?: (position: { latitude: number; longitude: number }) => void }) => (
    <button
      data-testid="map"
      onClick={() => props.onPositionPick?.({ latitude: 48.8, longitude: 2.3 })}
    />
  ),
}));
// The report is the findings page's dashboard, proven where it lives; here it only has to appear.
vi.mock('../components/dashboard/DashboardSections', () => ({
  DashboardSections: () => <div data-testid="dashboard" />,
}));
vi.mock('../hooks/useDashboard', () => ({ useResolveContext: () => ({}) }));
vi.mock('../api/dashboardLocalization', () => ({
  localizeSpec: () => ({ sections: [] }),
}));
vi.mock('../publicFindings/answeredFindings', () => ({
  findingsFrom: () => ({ spec: {}, scopeId: 'site-1', index: {}, reads: {} }),
}));

import { intakeApi } from '../api/intakeApi';
import { findingsApi } from '../api/findingsApi';
import { ExplorePage } from './ExplorePage';

const aRing = [
  { latitude: 48.801, longitude: 2.301 },
  { latitude: 48.802, longitude: 2.301 },
  { latitude: 48.802, longitude: 2.303 },
];

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(intakeApi.configured).mockReturnValue(true);
  vi.mocked(intakeApi.formOptions).mockResolvedValue({
    allocationCategories: ['residential', 'food-and-agriculture'],
    basemapSources: [],
    hazardTypes: ['river-flood'],
    hazardLevels: ['high', 'low'],
    defaultProgramme: [
      { category: 'residential', sharePct: 40 },
      { category: 'food-and-agriculture', sharePct: 60 },
    ],
    parcelLookup: true,
    placeSearch: true,
  });
  vi.mocked(intakeApi.parcelAt).mockResolvedValue({ boundary: aRing, attribution: '© the register' });
  vi.mocked(intakeApi.askForCode).mockResolvedValue(undefined);
  vi.mocked(intakeApi.exchangeTicket).mockResolvedValue('ticket-1');
  vi.mocked(intakeApi.submitWithTicket).mockResolvedValue({
    accepted: { reference: 'sub-ref-1' },
    ticket: 'ticket-2',
  });
  vi.mocked(findingsApi.readWithTicket).mockResolvedValue({
    findings: { spec: '{}', scopeId: 'site-1', things: [], relationships: [], ranges: {} },
    ticket: 'ticket-3',
  });
});

async function pickTheLand(): Promise<void> {
  render(<ExplorePage />);
  fireEvent.click(await screen.findByTestId('map'));
  await waitFor(() => expect(intakeApi.parcelAt).toHaveBeenCalledWith(48.8, 2.3));
}

async function reachTheReport(): Promise<void> {
  await pickTheLand();
  fireEvent.change(await screen.findByLabelText('Your name'), { target: { value: 'Ana Ferreira' } });
  fireEvent.change(screen.getByLabelText('Email address'), {
    target: { value: 'ana.ferreira@example.pt' },
  });
  fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));
  fireEvent.change(await screen.findByLabelText('Code'), { target: { value: '314159' } });
  fireEvent.click(screen.getByRole('button', { name: 'Show the report' }));
  await screen.findByTestId('dashboard');
}

describe('the plot-first page', () => {
  it('turns a click into the register boundary, and only then asks who is asking', async () => {
    await pickTheLand();

    expect(await screen.findByText(/found in the land register/)).toBeInTheDocument();
    expect(screen.getByText(/© the register/)).toBeInTheDocument();
    expect(screen.getByLabelText('Your name')).toBeInTheDocument();
  });

  it('offers drawing where no register holds a parcel, and asks nothing yet', async () => {
    vi.mocked(intakeApi.parcelAt).mockResolvedValue(null);

    await pickTheLand();

    expect(await screen.findByText(/Click corner by corner/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Your name')).not.toBeInTheDocument();
  });

  it('posts the exploration once the code is answered and lands on the report unasked', async () => {
    await reachTheReport();

    expect(intakeApi.exchangeTicket).toHaveBeenCalledWith('ana.ferreira@example.pt', '314159');
    const [document, ticket] = vi.mocked(intakeApi.submitWithTicket).mock.calls[0];
    expect(ticket).toBe('ticket-1');
    expect(document.parcel?.boundarySource).toBe('fetched-from-register');
    expect(document.allocations).toEqual([
      { category: 'residential', sharePct: 40 },
      { category: 'food-and-agriculture', sharePct: 60 },
    ]);
    expect(document.site.population).toBeUndefined();

    // The read rides the ticket the accepted submission handed back — nothing is retyped.
    expect(vi.mocked(findingsApi.readWithTicket).mock.calls[0][2]).toBe('ticket-2');
    expect(screen.getByText('sub-ref-1')).toBeInTheDocument();
  });

  it('re-posts the same submission when a dial settles, riding the renewed ticket', async () => {
    await reachTheReport();

    fireEvent.change(screen.getByLabelText('Residents'), { target: { value: '320' } });

    await waitFor(() => expect(intakeApi.submitWithTicket).toHaveBeenCalledTimes(2));
    const [document, ticket] = vi.mocked(intakeApi.submitWithTicket).mock.calls[1];
    expect(document.submissionId).toBe(vi.mocked(intakeApi.submitWithTicket).mock.calls[0][0].submissionId);
    expect(document.site.population).toBe(320);
    expect(ticket).toBe('ticket-3');
  });
});
