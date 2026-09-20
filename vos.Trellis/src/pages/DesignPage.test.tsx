import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import type { VosThing, VosRelationship } from '../types/vos';
import { MULTI_STEP_FORM_TEST_TIMEOUT_MILLISECONDS } from '../testTimeouts';

vi.setConfig({ testTimeout: MULTI_STEP_FORM_TEST_TIMEOUT_MILLISECONDS });

vi.mock('../hooks/useSse', () => ({
  useSse: () => ({ connected: true, on: () => () => {} }),
  useSubscription: () => {},
}));
vi.mock('../api/thingApi', () => ({
  thingApi: { setProperty: vi.fn(), remove: vi.fn() },
}));
vi.mock('../api/relationshipApi', () => ({ relationshipApi: { remove: vi.fn() } }));
vi.mock('../api/rangeApi', () => ({ rangeApi: { getAll: vi.fn().mockResolvedValue({ OwnRanges: [{ Name: 'flowing' }], InheritedRanges: [] }) } }));
vi.mock('../api/endpointApi', () => ({ endpointApi: { getAll: vi.fn().mockResolvedValue([]) } }));
vi.mock('../api/modelApi', () => ({ modelApi: { applyFragment: vi.fn() } }));
vi.mock('../api/stateApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/stateApi')>()),
  stateApi: { getThingsInState: vi.fn(), getStateTransitions: vi.fn() },
}));

import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { modelApi } from '../api/modelApi';
import { useModelStore } from '../stores/modelStore';
import { WIDGET_KINDS } from '../utils/gridLayout';
import { en } from '../i18n/locales/en';
import { DASHBOARD_SPEC_PROPERTY } from '../types/dashboard';
import { DesignPage } from './DesignPage';

const SEEDED = {
  title: 'Springs',
  icon: 'droplets',
  sections: [
    {
      title: 'Headline',
      layout: 'kpi-strip',
      widgets: [
        { type: 'kpi', title: 'Flowing', value: { kind: 'const', value: 4 }, format: 'integer' },
        { type: 'kpi', title: 'Dry', value: { kind: 'const', value: 1 } },
      ],
    },
  ],
};

const DESIGNED = {
  title: 'Reservoirs',
  icon: 'layout-template',
  designed: true,
  sections: [
    {
      title: 'Levels',
      layout: 'grid',
      widgets: [{ type: 'kpi', title: 'Full reservoirs', value: { kind: 'const', value: 2 }, placement: { column: 0, row: 0, width: 3, height: 3 } }],
    },
  ],
};

const thing = (Id: string, Name: string, Properties: Record<string, unknown> = {}, IsArchetype = false): VosThing => ({ Id, Name, Properties, IsArchetype });
const isA = (SubjectId: string, TargetId: string): VosRelationship => ({ Id: `${SubjectId}-is-${TargetId}`, SubjectId, PredicateId: 'is', TargetId, Properties: {} });

function seedStore() {
  useModelStore.setState({
    things: [
      thing('is', 'is'),
      thing('dashboard', 'Dashboard', {}, true),
      thing('page-springs', 'Springs', { [DASHBOARD_SPEC_PROPERTY]: JSON.stringify(SEEDED) }),
      thing('page-reservoirs', 'Reservoirs', { [DASHBOARD_SPEC_PROPERTY]: JSON.stringify(DESIGNED) }),
      thing('spring', 'Spring', {}, true),
      thing('s1', 'SPRING-1', { flow: 12.5 }),
    ],
    relationships: [isA('page-springs', 'dashboard'), isA('page-reservoirs', 'dashboard'), isA('s1', 'spring')],
    loaded: true,
  });
}

function ShownPath() {
  return <span data-testid="path">{useLocation().pathname}</span>;
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/design" element={<DesignPage />} />
        <Route path="/design/:dashboardKey" element={<DesignPage />} />
      </Routes>
      <ShownPath />
    </MemoryRouter>,
  );
}

const written = () => JSON.parse(vi.mocked(thingApi.setProperty).mock.calls[0][3] as string);

describe('DesignPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.stubGlobal('crypto', { randomUUID: () => 'minted-id' });
    vi.mocked(thingApi.setProperty).mockResolvedValue({ Id: 'page-reservoirs', Name: 'Reservoirs', Properties: {} });
    vi.mocked(modelApi.applyFragment).mockResolvedValue({ thingsCreated: 1, thingsUpdated: 0, relationshipsCreated: 1, things: [] });
    seedStore();
  });

  it('lists every page the model holds, saying which are the seed’s, and offers every kind of widget', () => {
    renderAt('/design/reservoirs');
    expect(screen.getByRole('button', { name: 'Open Springs' })).toHaveTextContent('Seeded');
    expect(screen.getByRole('button', { name: 'Open Reservoirs' })).toHaveTextContent('Kept');
    for (const kind of WIDGET_KINDS) {
      expect(screen.getByRole('button', { name: `Add a ${en.design.palette.kind[kind]}` })).toBeInTheDocument();
    }
  });

  it('draws the opened page on the canvas, each widget under its kind and title', () => {
    renderAt('/design/reservoirs');
    expect(screen.getByRole('button', { name: 'Select Full reservoirs' })).toHaveTextContent('Figure · Full reservoirs');
    expect(screen.getByRole('button', { name: 'Select section Levels' })).toBeInTheDocument();
  });

  it('adds a widget from the palette to the selected section, waiting for its binding', () => {
    renderAt('/design/reservoirs');
    fireEvent.click(screen.getByRole('button', { name: 'Add a Table' }));
    expect(screen.getByText('Waiting for: rows')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Select Untitled' })).toHaveTextContent('Table · Untitled');
  });

  it('retitles a selected widget from the properties panel', () => {
    renderAt('/design/reservoirs');
    fireEvent.click(screen.getByRole('button', { name: 'Select Full reservoirs' }));
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Reservoirs at capacity' } });
    fireEvent.blur(screen.getByLabelText('Title'));
    expect(screen.getByRole('button', { name: 'Select Reservoirs at capacity' })).toBeInTheDocument();
  });

  it('keeps a page the console kept by writing it back whole, marked as designed', async () => {
    renderAt('/design/reservoirs');
    fireEvent.click(screen.getByRole('button', { name: 'Add a Figure' }));
    fireEvent.click(screen.getByRole('button', { name: 'Keep' }));
    await waitFor(() => expect(thingApi.setProperty).toHaveBeenCalled());
    expect(vi.mocked(thingApi.setProperty).mock.calls[0].slice(0, 3)).toEqual(['page-reservoirs', 'spec', 'vos.String']);
    const kept = written();
    expect(kept.designed).toBe(true);
    expect(kept.sections[0].layout).toBe('grid');
    expect(kept.sections[0].widgets).toHaveLength(2);
    expect(kept.sections[0].widgets[1]).toMatchObject({ type: 'kpi', placement: { column: 0, row: 3, width: 3, height: 3 } });
  });

  it('opens a seeded page as a copy: no keeping in place, and keeping under a name writes a second Thing', async () => {
    renderAt('/design/springs');
    expect(screen.getByText(/This page is the seed/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Keep' })).toBeNull();
    fireEvent.change(screen.getByLabelText('Page name'), { target: { value: 'Springs copy' } });
    fireEvent.click(screen.getByRole('button', { name: 'Keep as a page' }));
    await waitFor(() => expect(modelApi.applyFragment).toHaveBeenCalled());
    expect(thingApi.setProperty).not.toHaveBeenCalled();
    const fragment = JSON.parse(vi.mocked(modelApi.applyFragment).mock.calls[0][0] as string);
    expect(fragment.Things[0].Name).toBe('Springs copy');
    const copy = JSON.parse(fragment.Things[0].Properties[DASHBOARD_SPEC_PROPERTY].value as string);
    expect(copy).toMatchObject({ title: 'Springs copy', designed: true });
    expect(copy.sections[0].layout).toBe('grid');
    expect(copy.sections[0].widgets.map((widget: { placement: unknown }) => widget.placement)).toEqual([
      { column: 0, row: 0, width: 6, height: 3 },
      { column: 6, row: 0, width: 6, height: 3 },
    ]);
  });

  it('refuses a name a page already stands under', () => {
    renderAt('/design/springs');
    fireEvent.change(screen.getByLabelText('Page name'), { target: { value: 'Reservoirs' } });
    expect(screen.getByRole('button', { name: 'Keep as a page' })).toBeDisabled();
    expect(screen.getByText(/already carries the name/)).toBeInTheDocument();
  });

  it('starts a new page from a name as one empty section', () => {
    renderAt('/design');
    expect(screen.getByText('Open a page on the left, or name a new one.')).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('New page name'), { target: { value: 'Catchments' } });
    fireEvent.click(screen.getByRole('button', { name: 'Start a page named Catchments' }));
    expect(screen.getByRole('heading', { name: 'Catchments' })).toBeInTheDocument();
    expect(screen.getByText('Drop a widget here')).toBeInTheDocument();
    expect(screen.getByTestId('path')).toHaveTextContent('/design');
  });

  it('adds, moves and removes a section from the properties panel', () => {
    renderAt('/design/reservoirs');
    fireEvent.click(screen.getByRole('button', { name: 'Add a section' }));
    expect(screen.getAllByText('Drop a widget here')).toHaveLength(1);
    fireEvent.click(screen.getByRole('button', { name: 'Earlier' }));
    const headings = screen.getAllByRole('button', { name: /Select section/ });
    expect(headings[0]).toHaveTextContent('Untitled section');
    expect(headings[1]).toHaveTextContent('Levels');
    fireEvent.click(screen.getByRole('button', { name: 'Remove section' }));
    expect(screen.getAllByRole('button', { name: /Select section/ })).toHaveLength(1);
  });

  it('binds a table to the Things of a kind chosen from the model, draws it, and keeps the keys its columns read', async () => {
    renderAt('/design/reservoirs');
    fireEvent.click(screen.getByRole('button', { name: 'Add a Table' }));
    fireEvent.click(screen.getByRole('button', { name: 'Add a column' }));
    fireEvent.change(screen.getAllByLabelText('Key')[0], { target: { value: 'flow' } });
    fireEvent.blur(screen.getAllByLabelText('Key')[0]);
    fireEvent.change(screen.getByLabelText('Rows'), { target: { value: 'stateList' } });
    fireEvent.change(screen.getByLabelText('Kind'), { target: { value: 'Spring' } });
    fireEvent.blur(screen.getByLabelText('Kind'));
    fireEvent.change(screen.getByLabelText('State'), { target: { value: 'flowing' } });
    fireEvent.blur(screen.getByLabelText('State'));
    expect(screen.queryByText('Waiting for: rows')).toBeNull();
    fireEvent.change(screen.getByLabelText('Rows shown'), { target: { value: '8' } });
    fireEvent.blur(screen.getByLabelText('Rows shown'));

    fireEvent.click(screen.getByRole('button', { name: 'Keep' }));
    await waitFor(() => expect(thingApi.setProperty).toHaveBeenCalled());
    const table = written().sections[0].widgets[1];
    expect(table).toMatchObject({ type: 'table', columns: [{ key: 'flow' }], rows: { kind: 'stateList', archetype: 'Spring', state: 'flowing', properties: ['flow'] } });
  });

  it('refuses to keep a page while a table states no row cap, and says so under the canvas', () => {
    renderAt('/design/reservoirs');
    expect(screen.getByText('The page can be kept.')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Add a Table' }));
    expect(screen.getByRole('button', { name: /states no row cap/ })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Keep' })).toBeDisabled();
  });

  it('opens the words in every language from the header and keeps one written into its locale', async () => {
    renderAt('/design/reservoirs');
    fireEvent.click(screen.getByRole('button', { name: 'The words in every language' }));
    const cell = screen.getByLabelText('Full reservoirs in Nederlands');
    fireEvent.change(cell, { target: { value: 'Volle reservoirs' } });
    fireEvent.blur(cell);
    fireEvent.click(screen.getByRole('button', { name: 'Keep' }));
    await waitFor(() => expect(thingApi.setProperty).toHaveBeenCalled());
    expect(written().translations).toEqual({ nl: { 'Full reservoirs': 'Volle reservoirs' } });
  });

  it('removes a kept page after asking, and leaves the designer on no page', async () => {
    vi.mocked(relationshipApi.remove).mockResolvedValue({ message: '' });
    vi.mocked(thingApi.remove).mockResolvedValue({ message: '' });
    renderAt('/design/reservoirs');
    fireEvent.click(screen.getByRole('button', { name: 'Remove page' }));
    fireEvent.click(screen.getByRole('button', { name: 'Remove' }));
    await waitFor(() => expect(thingApi.remove).toHaveBeenCalledWith('page-reservoirs'));
    expect(screen.getByTestId('path')).toHaveTextContent('/design');
  });
});
