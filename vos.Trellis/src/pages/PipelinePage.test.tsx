import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';

vi.mock('../hooks/useSse', () => ({ useSubscription: () => {} }));
vi.mock('../api/pipelineApi', () => ({ pipelineApi: { spawnAsync: vi.fn(), cancel: vi.fn() } }));
vi.mock('../api/modelApi', () => ({ modelApi: { applyFragment: vi.fn().mockResolvedValue({}) } }));
vi.mock('../api/thingApi', () => ({ thingApi: { create: vi.fn(), addProperty: vi.fn(), setProperty: vi.fn(), remove: vi.fn() } }));
vi.mock('../api/relationshipApi', () => ({ relationshipApi: { create: vi.fn(), setProperty: vi.fn(), remove: vi.fn() } }));

import { MemoryRouter } from 'react-router-dom';
import { useModelStore } from '../stores/modelStore';
import { ARCHETYPE_FLAG } from '../pipeline/model';
import { catalystFixture } from '../pipeline/catalysts.test.fixture';
import { installResizeObserverDouble } from '../testResizeObserver';
import { PipelinePage } from './PipelinePage';

describe('PipelinePage', () => {
  let restore: () => void;
  beforeEach(() => {
    restore = installResizeObserverDouble().restore;
    const { things, relationships } = catalystFixture();
    useModelStore.setState({ things, relationships, loaded: true });
  });
  afterEach(() => restore());

  it('opens the first pipeline on arrival, with its start node saying what it stands for', async () => {
    render(<PipelinePage />);
    await waitFor(() => expect(screen.getByRole('button', { name: /Digest/ })).toHaveAttribute('aria-current', 'true'));
    expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Digest');
    expect(screen.getByText('Reporting office', { selector: 'div' })).toBeInTheDocument();
    expect(screen.getByText('Nothing sends to Reporting office yet; what reaches this node is kept on the run\'s record.')).toBeInTheDocument();
  });

  it('refuses a start standing for something that starts nothing, in the findings under the canvas', async () => {
    // A pipeline saved with its start standing for a system that only receives: nothing on the page
    // offers such a start, but the model can hold one, and the drawing is judged when it opens.
    const { things, relationships, id } = catalystFixture();
    const named = (name: string) => things.find((thing) => thing.Name === name)!.Id;
    things.push({ Id: 'misdrawn', Name: 'Misdrawn', Properties: {} }, { Id: 'misdrawn-start', Name: 'From the office', Properties: { x: 0, y: 0 } });
    const relate = (subject: string, predicate: string, target: string) =>
      relationships.push({ Id: `${subject}-${predicate}-${target}`, Name: '', SubjectId: subject, PredicateId: predicate, TargetId: target, Properties: {} });
    relate('misdrawn', id.is, named('Workflow'));
    relate('misdrawn-start', id.is, named('Step'));
    relate('misdrawn-start', id.is, named('Doorway'));
    relate('misdrawn', id.has, 'misdrawn-start');
    relate('misdrawn-start', id.standsFor, id.reportingOffice);
    useModelStore.setState({ things, relationships, loaded: true });

    render(<PipelinePage />);
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Digest'));
    expect(screen.queryByText(/starts nothing/)).toBeNull();
    fireEvent.click(within(screen.getByRole('complementary', { name: 'Pipelines' })).getByRole('button', { name: /Misdrawn/ }));
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Misdrawn'));
    expect(screen.getByRole('list', { name: 'What stops this pipeline running' }))
      .toHaveTextContent("'From the office' is where this pipeline starts and stands for 'Reporting office', which starts nothing");
    expect(screen.getByRole('button', { name: 'Run' })).toBeDisabled();
  });

  it('links a run chosen from the history to the request it started from, and a run no request started to nothing', async () => {
    const { things, relationships, id } = catalystFixture();
    const relate = (subject: string, predicate: string, target: string) =>
      relationships.push({ Id: `${subject}-${predicate}-${target}`, Name: '', SubjectId: subject, PredicateId: predicate, TargetId: target, Properties: {} });
    things.push(
      { Id: 'of', Name: 'of', Properties: {} },
      { Id: 'run-kind', Name: 'Execution', IsArchetype: true, Properties: { [ARCHETYPE_FLAG.PipelineRun]: true } },
      { Id: 'from-a-door', Name: 'run 1', Properties: { status: 'succeeded', startedUtc: '2026-10-03T11:00:00Z', requestId: 'r-door' } },
      { Id: 'by-hand', Name: 'run 2', Properties: { status: 'failed', startedUtc: '2026-10-03T12:00:00Z' } },
    );
    for (const run of ['from-a-door', 'by-hand']) {
      relate(run, id.is, 'run-kind');
      relate(run, 'of', id.digest);
    }
    useModelStore.setState({ things, relationships, loaded: true });

    render(<MemoryRouter><PipelinePage /></MemoryRouter>);
    const history = await screen.findByRole('combobox', { name: 'Run history' });

    fireEvent.change(history, { target: { value: 'by-hand' } });
    expect(screen.queryByRole('link', { name: /Request/ })).toBeNull();

    fireEvent.change(history, { target: { value: 'from-a-door' } });
    expect(screen.getByRole('link', { name: /Request/ })).toHaveAttribute('href', '/requests?entry=r-door');
  });

  it('names a new pipeline from the roster and leaves the canvas empty for it', async () => {
    render(<PipelinePage />);
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Digest'));
    const field = screen.getByRole('textbox', { name: 'Name a new pipeline' });
    fireEvent.change(field, { target: { value: 'Night watch' } });
    fireEvent.keyDown(field, { key: 'Enter' });
    expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Night watch');
    expect(screen.getByText('Start a new pipeline')).toBeInTheDocument();
  });
});
