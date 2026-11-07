import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';

const declaredSubscriptions: unknown[] = [];
const hearsNothing = () => () => {};
vi.mock('../hooks/useSse', () => ({
  SUBSCRIPTION_OPENED: 'SubscriptionOpened',
  useSubscription: (selector: unknown) => { declaredSubscriptions.push(selector); },
  useSse: () => ({ connection: 'live', on: hearsNothing }),
  addToSubscription: vi.fn().mockResolvedValue(null),
  removeFromSubscription: vi.fn().mockResolvedValue(undefined),
}));
vi.mock('../api/pipelineApi', () => ({ pipelineApi: { spawnAsync: vi.fn(), cancel: vi.fn() } }));
vi.mock('../api/modelApi', () => ({ modelApi: { applyFragment: vi.fn().mockResolvedValue({}) } }));
vi.mock('../api/thingApi', () => ({ thingApi: { create: vi.fn(), addProperty: vi.fn(), setProperty: vi.fn(), remove: vi.fn() } }));
vi.mock('../api/relationshipApi', () => ({ relationshipApi: { create: vi.fn(), setProperty: vi.fn(), remove: vi.fn() } }));

import { MemoryRouter } from 'react-router-dom';
import { useModelStore } from '../stores/modelStore';
import { ROLE } from '../pipeline/model';
import { catalystFixture } from '../pipeline/catalysts.test.fixture';
import { installResizeObserverDouble } from '../testResizeObserver';
import { PIPELINE_PAGE } from '../pipeline/subscription';
import { addToSubscription, removeFromSubscription } from '../hooks/useSse';
import { PipelinePage } from './PipelinePage';

const SEARCH_THE_OFFERED_KINDS = 'Search every Thing of the kinds the ports offer, not only what they offer';

describe('PipelinePage', () => {
  let restore: () => void;
  beforeEach(() => {
    restore = installResizeObserverDouble().restore;
    vi.mocked(addToSubscription).mockClear();
    vi.mocked(removeFromSubscription).mockClear();
    const { things, relationships } = catalystFixture();
    useModelStore.setState({ things, relationships, loaded: true });
  });
  afterEach(() => restore());

  it('opens the first pipeline on arrival, saying its end stands for a system only a tell node wired on to it can tell', async () => {
    render(<PipelinePage />);
    await waitFor(() => expect(screen.getByRole('button', { name: /Digest/ })).toHaveAttribute('aria-current', 'true'));
    expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Digest');
    expect(screen.getByText('Reporting office', { selector: 'div' })).toBeInTheDocument();
    expect(within(screen.getByRole('list', { name: 'What stops this pipeline running' }))
      .getByText("'The office is told' is the Output node and stands for 'Reporting office'; a tell node wired on to it tells a system")).toBeInTheDocument();
  });

  it('places a follow-on node from a pipeline row, a tell node from a system row and a stop node with its kind’s ports', async () => {
    render(<PipelinePage />);
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Digest'));
    const kinds = () => [...document.querySelectorAll('.react-flow__node')].map((node) => node.querySelector('span.font-mono.text-zinc-400')?.textContent);

    fireEvent.click(screen.getByRole('button', { name: 'Place a follow-on node starting Refill' }));
    fireEvent.click(screen.getByRole('button', { name: 'Place a tell node telling Reporting office' }));
    fireEvent.click(screen.getByRole('button', { name: 'Place a stop node' }));

    await waitFor(() => expect(kinds().slice(-3)).toEqual(['follow-on', 'tell', 'stop']));
    const stop = [...document.querySelectorAll('.react-flow__node')].at(-1) as HTMLElement;
    expect(within(stop).getByText('things')).toBeInTheDocument();
    expect(within(stop).getByText('requested')).toBeInTheDocument();
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
      { Id: 'run-kind', Name: 'Execution', IsArchetype: true, Properties: {} },
      { Id: ROLE.PipelineRun, Name: 'pipelineRun role', Properties: {} },
      { Id: 'from-a-door', Name: 'run 1', Properties: { status: 'succeeded', startedUtc: '2026-10-03T11:00:00Z', requestId: 'r-door' } },
      { Id: 'by-hand', Name: 'run 2', Properties: { status: 'failed', startedUtc: '2026-10-03T12:00:00Z' } },
    );
    relate(ROLE.PipelineRun, id.binds, 'run-kind');
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

  it('shows a run paused at a waiting node as waiting, with the node marked, and offers Cancel, which stops a waiting run at once', async () => {
    const { things, relationships, id } = catalystFixture();
    const relate = (subject: string, predicate: string, target: string) =>
      relationships.push({ Id: `${subject}-${predicate}-${target}`, Name: '', SubjectId: subject, PredicateId: predicate, TargetId: target, Properties: {} });
    things.push(
      { Id: 'of', Name: 'of', Properties: {} },
      { Id: 'run-kind', Name: 'Execution', IsArchetype: true, Properties: {} },
      { Id: ROLE.PipelineRun, Name: 'pipelineRun role', Properties: {} },
      { Id: 'held-up', Name: 'run 3', Properties: { status: 'waiting', startedUtc: '2026-10-15T09:00:00Z' } },
      { Id: 'held-up-at-the-end', Name: 'The office is told run', Properties: { nodeId: id.digestEnd, status: 'waiting' } },
    );
    relate(ROLE.PipelineRun, id.binds, 'run-kind');
    relate('held-up', id.is, 'run-kind');
    relate('held-up', 'of', id.digest);
    relate('held-up', id.has, 'held-up-at-the-end');
    useModelStore.setState({ things, relationships, loaded: true });

    render(<MemoryRouter><PipelinePage /></MemoryRouter>);
    const history = await screen.findByRole('combobox', { name: 'Run history' });
    fireEvent.change(history, { target: { value: 'held-up' } });

    expect(screen.getByText('Run waiting…')).toHaveClass('text-amber-600');
    await waitFor(() => expect(screen.getByText('The office is told: waiting')).toBeInTheDocument());
    expect(document.querySelector('.ring-amber-400')).not.toBeNull();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Run' })).toBeNull();
  });

  it('saves an Output rule and a choice set in the node inspector, and the wire it already had taken on success', async () => {
    const { id } = catalystFixture();
    render(<PipelinePage />);
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Digest'));
    fireEvent.click(within(screen.getByRole('complementary', { name: 'Pipelines' })).getByRole('button', { name: /Readings arrive/ }));
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Readings arrive'));

    const canvasNode = (label: RegExp) =>
      [...document.querySelectorAll('.react-flow__node')].filter((node) => label.test(node.textContent ?? '')).at(-1)!;
    fireEvent.click(canvasNode(/The answer/));
    fireEvent.click(screen.getByText('+ add rule'));
    fireEvent.click(within(screen.getByRole('list', { name: 'Services' })).getByRole('button', { name: /intake/ }));
    fireEvent.click(canvasNode(/intake/));
    fireEvent.click(screen.getByText('+ add choice'));
    fireEvent.click(screen.getByText('+ add choice'));
    fireEvent.click(screen.getByLabelText('Remove choice choice1'));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    const { relationshipApi } = await import('../api/relationshipApi');
    const { modelApi } = await import('../api/modelApi');
    await waitFor(() => expect(vi.mocked(relationshipApi.create)).toHaveBeenCalledWith(id.readingsWire, id.takenOn, id.success));
    const written = JSON.parse(vi.mocked(modelApi.applyFragment).mock.calls.at(-1)![0]);
    expect(written.Things.find((thing: { Id: string }) => thing.Id === id.readingsEnd).Properties.when.value).toBe('$count(failures) = 0');
    expect(written.Things.filter((thing: { Name: string }) => thing.Name.startsWith('choice')).map((thing: { Name: string }) => thing.Name))
      .toEqual(['choice2']);
  });

  it('declares the Things its roles reach, never the whole model', () => {
    render(<PipelinePage />);
    expect(declaredSubscriptions).toContainEqual(PIPELINE_PAGE);
    expect(declaredSubscriptions).not.toContainEqual({ all: true });
  });

  it('saves what a node chose and a Thing its choice compares with, found by searching the offered kinds', async () => {
    const { id } = catalystFixture();
    render(<PipelinePage />);
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Digest'));

    fireEvent.click(within(screen.getByRole('list', { name: 'Services' })).getByRole('button', { name: /intake/ }));
    fireEvent.click([...document.querySelectorAll('.react-flow__node')].filter((node) => /intake/.test(node.textContent ?? '')).at(-1)!);
    fireEvent.change(screen.getByLabelText('Chosen for cadence'), { target: { value: id.daily } });
    fireEvent.click(screen.getByText('+ add choice'));
    fireEvent.click(screen.getByLabelText(SEARCH_THE_OFFERED_KINDS));
    const addCompared = screen.getByPlaceholderText('Add a Thing for choice 1 to compare with');
    fireEvent.change(addCompared, { target: { value: 'Weekly' } });
    fireEvent.click(within(addCompared.parentElement!).getByText('Weekly'));
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    const { relationshipApi } = await import('../api/relationshipApi');
    await waitFor(() => expect(vi.mocked(relationshipApi.create)).toHaveBeenCalledWith(expect.any(String), id.offers, id.daily));
    expect(vi.mocked(relationshipApi.create)).toHaveBeenCalledWith(expect.any(String), id.comparesWith, id.weekly);
  });

  // The page holds only what its roles reach, so the wider list is asked of the platform by the kinds
  // the node's in ports offer, and what it answers is listed with a count — and taken away again when
  // the search ends, except what the drawing now compares with.
  it('asks the subscription for the offered kinds while a choice searches them, lists what arrives, and lets it go after', async () => {
    const { id } = catalystFixture();
    const stale = { Id: 'stale', Name: 'Fortnightly', Properties: {} };
    vi.mocked(addToSubscription).mockResolvedValueOnce({
      things: [stale],
      relationships: [{ Id: 'stale-is', Name: '', SubjectId: 'stale', PredicateId: id.is, TargetId: id.rhythm, Properties: {} }],
      thingStates: new Map(),
    });
    render(<PipelinePage />);
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Digest'));
    fireEvent.click(within(screen.getByRole('list', { name: 'Services' })).getByRole('button', { name: /intake/ }));
    fireEvent.click([...document.querySelectorAll('.react-flow__node')].filter((node) => /intake/.test(node.textContent ?? '')).at(-1)!);
    fireEvent.click(screen.getByText('+ add choice'));
    expect(addToSubscription).not.toHaveBeenCalled();

    fireEvent.click(screen.getByLabelText(SEARCH_THE_OFFERED_KINDS));

    await waitFor(() => expect(addToSubscription).toHaveBeenCalledWith({ typeIds: [id.rhythm] }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('4 Things of those kinds'));
    const addCompared = screen.getByPlaceholderText('Add a Thing for choice 1 to compare with');
    fireEvent.change(addCompared, { target: { value: 'Fortnightly' } });
    fireEvent.click(within(addCompared.parentElement!).getByText('Fortnightly'));

    expect(screen.getByLabelText(SEARCH_THE_OFFERED_KINDS)).not.toBeChecked();
    expect(screen.getByLabelText('Stop comparing with Fortnightly')).toBeInTheDocument();
    expect(removeFromSubscription).not.toHaveBeenCalled();
    expect(useModelStore.getState().things.some((thing) => thing.Id === 'stale')).toBe(true);
  });

  it('shows what a saved node chose and keeps it through a save', async () => {
    const { things, relationships, id } = catalystFixture();
    const relate = (subject: string, predicate: string, target: string) =>
      relationships.push({ Id: `${subject}-${predicate}-${target}`, Name: '', SubjectId: subject, PredicateId: predicate, TargetId: target, Properties: {} });
    things.push({ Id: 'reads-daily', Name: 'Reads daily', Properties: { x: 200, y: 0 } });
    relate('reads-daily', id.is, things.find((thing) => thing.Name === 'Step')!.Id);
    relate('reads-daily', id.has, id.intakeDoor);
    relate(id.digest, id.has, 'reads-daily');
    relate('reads-daily', id.offers, id.daily);
    useModelStore.setState({ things, relationships, loaded: true });

    render(<PipelinePage />);
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Digest'));
    fireEvent.click([...document.querySelectorAll('.react-flow__node')].find((node) => /Reads daily/.test(node.textContent ?? ''))!);
    expect(screen.getByLabelText('Chosen for cadence')).toHaveValue(id.daily);
    fireEvent.click(screen.getByRole('button', { name: 'Save' }));

    const { modelApi } = await import('../api/modelApi');
    const { relationshipApi } = await import('../api/relationshipApi');
    await waitFor(() => expect(vi.mocked(modelApi.applyFragment)).toHaveBeenCalled());
    expect(vi.mocked(relationshipApi.remove)).not.toHaveBeenCalledWith(`reads-daily-${id.offers}-${id.daily}`);
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
