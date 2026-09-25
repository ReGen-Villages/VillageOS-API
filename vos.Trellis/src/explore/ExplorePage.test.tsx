import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
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
vi.mock('../api/findingsApi', () => ({
  findingsApi: {
    readWithTicket: vi.fn(), reduceWithTicket: vi.fn(), shareDocumentWithTicket: vi.fn(), listDocumentsWithTicket: vi.fn(),
  },
}));
// The map cannot build in a test's document, and what this page owes it is only the wiring: the pick
// handler when no position exists, and the boundary when one does.
vi.mock('../components/map/MapView', () => ({
  MapView: (props: {
    onPositionPick?: (position: { latitude: number; longitude: number }) => void;
    onBoundaryChange?: (boundary: { latitude: number; longitude: number }[]) => void;
  }) => (
    <>
      <button
        data-testid="map"
        onClick={() => props.onPositionPick?.({ latitude: 48.8, longitude: 2.3 })}
      />
      {props.onBoundaryChange && (
        <button data-testid="draw" onClick={() => props.onBoundaryChange?.(aRing)} />
      )}
    </>
  ),
}));
// The report is the findings page's dashboard, proven where it lives; here it only has to appear, and
// to be handed the sections the tiles did not take.
vi.mock('../components/dashboard/DashboardSections', () => ({
  DashboardSections: vi.fn(() => <div data-testid="dashboard" />),
}));
vi.mock('../hooks/useDashboard', () => ({
  useResolveContext: () => ({}),
  useBinding: (binding?: { value?: unknown }) => ({ loading: false, error: false, value: binding?.value ?? null }),
  useBindings: (bindings: { value?: unknown }[]) =>
    bindings.map((binding) => ({ loading: false, error: false, value: binding?.value ?? null })),
}));
vi.mock('../api/dashboardLocalization', () => ({
  localizeSpecification: vi.fn(() => ({ sections: [] })),
}));
vi.mock('../publicFindings/answeredFindings', () => ({
  findingsFrom: vi.fn(() => ({ specification: {}, scopeId: 'site-1', index: {}, reads: {} })),
}));

import { intakeApi } from '../api/intakeApi';
import { findingsApi } from '../api/findingsApi';
import { localizeSpecification } from '../api/dashboardLocalization';
import { findingsFrom } from '../publicFindings/answeredFindings';
import { DashboardSections } from '../components/dashboard/DashboardSections';
import { useMapStore } from '../stores/mapStore';
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
    themes: [{ name: 'Temperature', colour: '#F0A840', icon: 'thermometer', order: 1 }],
  });
  vi.mocked(localizeSpecification).mockReturnValue({ title: 'Site submission', sections: [] });
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
  vi.mocked(findingsApi.listDocumentsWithTicket).mockResolvedValue({ documents: [], ticket: 'ticket-3' });
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

  // The report replaces the sections the person was working in, so a page left where they had scrolled
  // to opens partway down it — past the figures it leads with, which reads as a report holding nothing.
  it('opens the report at the top of it', async () => {
    const scrolled = vi.fn();
    vi.stubGlobal('scrollTo', scrolled);

    await reachTheReport();

    expect(scrolled).toHaveBeenCalledWith(expect.objectContaining({ top: 0 }));
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

describe('the facts beside the map', () => {
  it('shows the area and the coordinates as cards once the land is picked, each naming its source', async () => {
    await pickTheLand();

    const area = screen.getByRole('group', { name: 'Area' });
    expect(within(area).getByText(/hectares/)).toBeInTheDocument();
    expect(within(area).getByText(/acres/)).toBeInTheDocument();
    expect(within(area).getByText('© the register')).toBeInTheDocument();

    const coordinates = screen.getByRole('group', { name: 'Coordinates' });
    expect(within(coordinates).getByText('48°48′00.00″ N 2°18′00.00″ E')).toBeInTheDocument();
    expect(within(coordinates).getByText('clicked on the map')).toBeInTheDocument();
  });

  it('says a drawn boundary was drawn by hand', async () => {
    vi.mocked(intakeApi.parcelAt).mockResolvedValue(null);
    await pickTheLand();
    await screen.findByText(/Click corner by corner/);
    expect(screen.queryByRole('group', { name: 'Area' })).toBeNull();

    fireEvent.click(screen.getByTestId('draw'));

    expect(within(screen.getByRole('group', { name: 'Area' })).getByText('drawn by hand')).toBeInTheDocument();
  });

  it('adds the reference as a card once the submission is accepted, and the model\'s own facts beside it', async () => {
    vi.mocked(localizeSpecification).mockReturnValue({
      title: 'Site submission',
      sections: [
        {
          title: 'The land',
          facts: true,
          widgets: [{ type: 'kpi', title: 'Elevation', value: { kind: 'const', value: 1431 }, format: 'integer', unit: 'm' }],
        },
        { title: 'Balances', widgets: [] },
      ],
    });

    await reachTheReport();

    const reference = screen.getByRole('group', { name: 'Reference' });
    expect(within(reference).getByText('sub-ref-1')).toBeInTheDocument();
    const elevation = screen.getByRole('group', { name: 'Elevation' });
    expect(within(elevation).getByText('1,431')).toBeInTheDocument();
    const handed = vi.mocked(DashboardSections).mock.calls.at(-1)![0].sections;
    expect(handed.map((section) => section.title)).toEqual(['Balances']);
  });
});

describe('the report as tiles', () => {
  it('draws a section naming a theme as a tile and hands the rest to the list', async () => {
    vi.mocked(localizeSpecification).mockReturnValue({
      title: 'Site submission',
      sections: [
        { title: 'Temperature', theme: 'Temperature', widgets: [] },
        { title: 'Balances', widgets: [] },
      ],
    });

    await reachTheReport();

    expect(screen.getByRole('button', { name: 'Temperature' })).toHaveStyle({ backgroundColor: '#F0A840' });
    const handed = vi.mocked(DashboardSections).mock.calls.at(-1)![0].sections;
    expect(handed.map((section) => section.title)).toEqual(['Balances']);
  });

  it('renders a spec naming no theme exactly as before', async () => {
    vi.mocked(localizeSpecification).mockReturnValue({
      title: 'Site submission',
      sections: [{ title: 'Balances', widgets: [] }],
    });

    await reachTheReport();

    expect(screen.queryByRole('button', { name: 'Temperature' })).toBeNull();
    const handed = vi.mocked(DashboardSections).mock.calls.at(-1)![0].sections;
    expect(handed.map((section) => section.title)).toEqual(['Balances']);
  });
});

describe('the history a chart asks for', () => {
  it('is reduced through the intake service under the ticket the page holds, and the renewal is kept', async () => {
    vi.mocked(findingsApi.reduceWithTicket).mockResolvedValue({
      answer: { Groups: [{ Key: '1', Value: 27.4 }], Samples: 8760, UnusableSamples: 0 }, ticket: 'ticket-4',
    });
    await reachTheReport();
    const [, reduce] = vi.mocked(findingsFrom).mock.calls.at(-1)!;
    const question = {
      thingId: 'site-1', property: 'temperature', windowSeconds: 31_536_000,
      steps: [{ fold: 'monthOfYear' as const, function: 'Max' as const }],
    };

    await expect(reduce!(question)).resolves.toEqual({ Groups: [{ Key: '1', Value: 27.4 }], Samples: 8760, UnusableSamples: 0 });

    const [submissionId, ticket, asked] = vi.mocked(findingsApi.reduceWithTicket).mock.calls[0];
    expect(submissionId).toBe(vi.mocked(intakeApi.submitWithTicket).mock.calls[0][0].submissionId);
    expect(ticket).toBe('ticket-3');
    expect(asked).toEqual({ property: 'temperature', windowSeconds: 31_536_000, steps: question.steps });

    // The next read rides the ticket the reduction handed back.
    fireEvent.click(screen.getByRole('button', { name: 'Read again' }));
    await waitFor(() => expect(vi.mocked(findingsApi.readWithTicket).mock.calls.at(-1)![2]).toBe('ticket-4'));
  });
});

// After the report, the page asks for surveys: the bytes go to the intake service under the ticket the
// page holds, each with the description typed beside it, and what the model holds is listed after.
describe('the surveys asked for after the report', () => {
  const SOIL = { id: 'doc-1', fileName: 'soil.pdf', description: 'The soil test', contentType: 'application/pdf', sizeBytes: 4, sharedAt: '2026-09-18T10:00:00Z' };
  const WATER = { id: 'doc-2', fileName: 'water.csv', description: 'Well readings', contentType: 'text/csv', sizeBytes: 6, sharedAt: '2026-09-18T10:01:00Z' };

  function chooseFiles(...files: File[]): void {
    fireEvent.change(screen.getByLabelText('Choose files'), { target: { files } });
  }

  it('is not asked before there is a report', async () => {
    await pickTheLand();

    expect(screen.queryByText('Have you done any surveys which you can share?')).toBeNull();
  });

  it('asks once the report is up, and yes opens the file box with a description per file', async () => {
    await reachTheReport();
    expect(screen.getByText('Have you done any surveys which you can share?')).toBeInTheDocument();
    expect(screen.queryByLabelText('Choose files')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Yes, share files' }));
    chooseFiles(new File(['soil'], 'soil.pdf', { type: 'application/pdf' }), new File(['w'], 'water.csv', { type: 'text/csv' }));

    expect(screen.getByText('soil.pdf')).toBeInTheDocument();
    expect(screen.getByText('water.csv')).toBeInTheDocument();
    expect(screen.getAllByLabelText('What is this file?')).toHaveLength(2);
  });

  it('not now closes the question without sending anything', async () => {
    await reachTheReport();

    fireEvent.click(screen.getByRole('button', { name: 'Not now' }));

    expect(screen.queryByText('Have you done any surveys which you can share?')).toBeNull();
    expect(findingsApi.shareDocumentWithTicket).not.toHaveBeenCalled();
  });

  it('sends each file with its description under the ticket the page holds, shows progress, and lists what the model now holds', async () => {
    vi.mocked(findingsApi.shareDocumentWithTicket).mockImplementation(async (_id, _ticket, file, _description, onProgress) => {
      onProgress(0.5);
      return { document: file.name === 'soil.pdf' ? SOIL : WATER, ticket: `ticket-after-${file.name}` };
    });
    await reachTheReport();
    fireEvent.click(screen.getByRole('button', { name: 'Yes, share files' }));
    chooseFiles(new File(['soil'], 'soil.pdf', { type: 'application/pdf' }), new File(['w'], 'water.csv', { type: 'text/csv' }));
    const [soil, water] = screen.getAllByLabelText('What is this file?');
    fireEvent.change(soil, { target: { value: 'The soil test' } });
    fireEvent.change(water, { target: { value: 'Well readings' } });

    fireEvent.click(screen.getByRole('button', { name: 'Share the files' }));

    await waitFor(() => expect(findingsApi.shareDocumentWithTicket).toHaveBeenCalledTimes(2));
    const calls = vi.mocked(findingsApi.shareDocumentWithTicket).mock.calls;
    expect(calls[0][0]).toBe(vi.mocked(intakeApi.submitWithTicket).mock.calls[0][0].submissionId);
    expect(calls[0][1]).toBe('ticket-3');
    expect(calls[0][2].name).toBe('soil.pdf');
    expect(calls[0][3]).toBe('The soil test');
    // One file after another, each riding the ticket the last one handed back.
    expect(calls[1][1]).toBe('ticket-after-soil.pdf');
    expect(calls[1][3]).toBe('Well readings');

    const listed = await screen.findByRole('list', { name: 'Files you have shared' });
    expect(within(listed).getByText('soil.pdf')).toBeInTheDocument();
    expect(within(listed).getByText('The soil test')).toBeInTheDocument();
    expect(within(listed).getByText('water.csv')).toBeInTheDocument();
    expect(within(listed).getByText('Well readings')).toBeInTheDocument();

    // The next read rides the ticket the last share handed back.
    fireEvent.click(screen.getByRole('button', { name: 'Read again' }));
    await waitFor(() => expect(vi.mocked(findingsApi.readWithTicket).mock.calls.at(-1)![2]).toBe('ticket-after-water.csv'));
  });

  it('names the reason beside a file the service refuses, and sends nothing for one over the limit', async () => {
    vi.mocked(findingsApi.shareDocumentWithTicket).mockRejectedValue(new Error('Files are not taken here: the model declares no place for one.'));
    await reachTheReport();
    fireEvent.click(screen.getByRole('button', { name: 'Yes, share files' }));
    const huge = new File(['x'], 'atlas.tif', { type: 'image/tiff' });
    Object.defineProperty(huge, 'size', { value: 25 * 1024 * 1024 + 1 });
    chooseFiles(new File(['soil'], 'soil.pdf', { type: 'application/pdf' }), huge);
    expect(screen.getByText('Not shared: A file may be at most 25 MB.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Share the files' }));

    expect(await screen.findByText('Not shared: Files are not taken here: the model declares no place for one.')).toBeInTheDocument();
    expect(findingsApi.shareDocumentWithTicket).toHaveBeenCalledTimes(1);
  });

  it('lists the files already shared when the report opens, without asking again for what is listed', async () => {
    vi.mocked(findingsApi.listDocumentsWithTicket).mockResolvedValue({ documents: [SOIL], ticket: 'ticket-listed' });

    await reachTheReport();

    const listed = await screen.findByRole('list', { name: 'Files you have shared' });
    expect(within(listed).getByText('soil.pdf')).toBeInTheDocument();
    expect(within(listed).getByText('The soil test')).toBeInTheDocument();
    expect(vi.mocked(findingsApi.listDocumentsWithTicket).mock.calls[0][1]).toBe('ticket-3');
  });

  it('asks for the mailbox again when the ticket aged out before a file was sent', async () => {
    vi.mocked(findingsApi.shareDocumentWithTicket).mockRejectedValue(new Error('The ticket has expired. Verify the address again.'));
    await reachTheReport();
    fireEvent.click(screen.getByRole('button', { name: 'Yes, share files' }));
    chooseFiles(new File(['soil'], 'soil.pdf', { type: 'application/pdf' }));

    fireEvent.click(screen.getByRole('button', { name: 'Share the files' }));

    expect(await screen.findByText(/confirm your mailbox again/)).toBeInTheDocument();
  });
});

// The closing view: the parcel on the imagery the model declares, with the sections the page declares
// as tabs in a sheet along the bottom.
describe('the overview over the land', () => {
  const TABS = [
    { title: 'Location', tab: 'location', layout: 'kpi-strip' as const, widgets: [] },
    { title: 'Water', tab: 'water', layout: 'kpi-strip' as const, widgets: [] },
    { title: 'Nutrition', tab: 'nutrition', layout: 'kpi-strip' as const, widgets: [] },
    { title: 'Housing', tab: 'housing', layout: 'kpi-strip' as const, widgets: [] },
    { title: 'Infrastructure', tab: 'infrastructure', layout: 'kpi-strip' as const, widgets: [] },
    { title: 'Overview', tab: 'overview', layout: 'kpi-strip' as const, widgets: [] },
  ];
  const IMAGERY = { id: 'src-2', name: 'Satellite', attribution: 'Example imagery', kind: 'raster' as const, tileUrl: 'https://example.test/{z}/{x}/{y}', maximumZoom: 19 };
  const STREETS = { id: 'src-1', name: 'Streets', attribution: 'Example', kind: 'style' as const, styleUrl: 'https://example.test/s.json' };

  beforeEach(() => {
    vi.mocked(localizeSpecification).mockReturnValue({ title: 'Site submission', sections: [{ title: 'Balances', widgets: [] }, ...TABS] });
    useMapStore.setState({ selectedSourceName: null });
  });

  it('keeps the tab sections out of the report beneath, and offers the overview once the report is up', async () => {
    await reachTheReport();

    const handed = vi.mocked(DashboardSections).mock.calls.at(-1)![0].sections;
    expect(handed.map((section) => section.title)).toEqual(['Balances']);
    expect(screen.getByRole('button', { name: 'See it on the land' })).toBeInTheDocument();
  });

  it('draws the six tabs in order over the map, the first open, and each tab its own section', async () => {
    await reachTheReport();

    fireEvent.click(screen.getByRole('button', { name: 'See it on the land' }));

    const sheet = screen.getByRole('tablist');
    expect(within(sheet).getAllByRole('tab').map((tab) => tab.textContent))
      .toEqual(['Location', 'Water', 'Nutrition', 'Housing', 'Infrastructure', 'Overview']);
    expect(within(sheet).getByRole('tab', { name: 'Location' })).toHaveAttribute('aria-selected', 'true');
    expect(vi.mocked(DashboardSections).mock.calls.at(-1)![0].sections.map((section) => section.tab)).toEqual(['location']);

    fireEvent.click(within(sheet).getByRole('tab', { name: 'Water' }));

    expect(within(sheet).getByRole('tab', { name: 'Water' })).toHaveAttribute('aria-selected', 'true');
    expect(vi.mocked(DashboardSections).mock.calls.at(-1)![0].sections.map((section) => section.tab)).toEqual(['water']);
  });

  it('opens on the imagery the model declares and gives the map back its layer on closing', async () => {
    vi.mocked(intakeApi.formOptions).mockResolvedValue({
      ...(await intakeApi.formOptions()),
      basemapSources: [IMAGERY, STREETS],
    });
    await reachTheReport();
    useMapStore.getState().selectSource('Streets');

    fireEvent.click(screen.getByRole('button', { name: 'See it on the land' }));
    expect(useMapStore.getState().selectedSourceName).toBe('Satellite');

    fireEvent.click(screen.getByRole('button', { name: 'Back to the report' }));

    expect(useMapStore.getState().selectedSourceName).toBe('Streets');
    expect(screen.queryByRole('tablist')).toBeNull();
  });

  it('is not offered where the page declares no tab', async () => {
    vi.mocked(localizeSpecification).mockReturnValue({ title: 'Site submission', sections: [{ title: 'Balances', widgets: [] }] });

    await reachTheReport();

    expect(screen.queryByRole('button', { name: 'See it on the land' })).toBeNull();
  });
});

describe('the page on a phone', () => {
  it('asks the phone for the keyboard each claim field needs, and offers the code the mail brought', async () => {
    await pickTheLand();

    expect(await screen.findByLabelText('Your name')).toHaveAttribute('autocomplete', 'name');
    expect(screen.getByLabelText('Email address')).toHaveAttribute('type', 'email');
    expect(screen.getByLabelText('Email address')).toHaveAttribute('autocomplete', 'email');

    fireEvent.change(screen.getByLabelText('Your name'), { target: { value: 'Ana Ferreira' } });
    fireEvent.change(screen.getByLabelText('Email address'), { target: { value: 'ana.ferreira@example.pt' } });
    fireEvent.click(screen.getByRole('button', { name: 'Send a code' }));

    const code = await screen.findByLabelText('Code');
    expect(code).toHaveAttribute('inputmode', 'numeric');
    expect(code).toHaveAttribute('autocomplete', 'one-time-code');
  });

  it('keeps the send-code label on one line beside what is still missing', async () => {
    await pickTheLand();

    expect(await screen.findByRole('button', { name: 'Send a code' })).toHaveClass('whitespace-nowrap');
  });
});
