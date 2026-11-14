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
vi.mock('../pipeline/stop', () => ({ stopRun: vi.fn().mockResolvedValue(undefined) }));

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
    relate('misdrawn-start', id.is, named('Beginning'));
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
      { Id: 'from-a-request', Name: 'run 1', Properties: { status: 'succeeded', startedUtc: '2026-10-03T11:00:00Z', requestId: 'r-request' } },
      { Id: 'by-hand', Name: 'run 2', Properties: { status: 'failed', startedUtc: '2026-10-03T12:00:00Z' } },
    );
    relate(ROLE.PipelineRun, id.binds, 'run-kind');
    for (const run of ['from-a-request', 'by-hand']) {
      relate(run, id.is, 'run-kind');
      relate(run, 'of', id.digest);
    }
    useModelStore.setState({ things, relationships, loaded: true });

    render(<MemoryRouter><PipelinePage /></MemoryRouter>);
    const history = await screen.findByRole('combobox', { name: 'Run history' });

    fireEvent.change(history, { target: { value: 'by-hand' } });
    expect(screen.queryByRole('link', { name: /Request/ })).toBeNull();

    fireEvent.change(history, { target: { value: 'from-a-request' } });
    expect(screen.getByRole('link', { name: /Request/ })).toHaveAttribute('href', '/requests?entry=r-request');
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
    relate('reads-daily', id.has, id.intakeConnection);
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

  describe('what the toolbar, the canvas and the panels under it do', () => {
    const canvasNodes = () => [...document.querySelectorAll('.react-flow__node')] as HTMLElement[];
    const canvasNode = (label: RegExp) => canvasNodes().filter((node) => label.test(node.textContent ?? '')).at(-1)!;
    const placeTheIntakeService = () =>
      fireEvent.click(within(screen.getByRole('list', { name: 'Services' })).getByRole('button', { name: /intake/ }));

    async function openReadingsArrive() {
      render(<MemoryRouter><PipelinePage /></MemoryRouter>);
      await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Digest'));
      fireEvent.click(within(screen.getByRole('complementary', { name: 'Pipelines' })).getByRole('button', { name: /Readings arrive/ }));
      await waitFor(() => expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Readings arrive'));
      await waitFor(() => expect(canvasNodes()).toHaveLength(2));
    }

    it('takes back the last change with Undo, and the canvas stays unsaved', async () => {
      await openReadingsArrive();
      expect(screen.getByRole('button', { name: 'Undo' })).toBeDisabled();
      expect(screen.getByText('saved')).toBeInTheDocument();

      placeTheIntakeService();
      await waitFor(() => expect(canvasNodes()).toHaveLength(3));
      expect(screen.getByText('unsaved changes')).toBeInTheDocument();

      fireEvent.click(screen.getByRole('button', { name: 'Undo' }));
      await waitFor(() => expect(canvasNodes()).toHaveLength(2));
      expect(screen.getByText('unsaved changes')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Undo' })).toBeDisabled();
    });

    it('undoes on Ctrl or Cmd+Z, but not inside a text box, with Shift held, or for another key', async () => {
      await openReadingsArrive();
      placeTheIntakeService();
      await waitFor(() => expect(canvasNodes()).toHaveLength(3));

      fireEvent.keyDown(screen.getByRole('textbox', { name: 'Pipeline name' }), { key: 'z', ctrlKey: true });
      fireEvent.keyDown(window, { key: 'z', ctrlKey: true, shiftKey: true });
      fireEvent.keyDown(window, { key: 'y', ctrlKey: true });
      fireEvent.keyDown(window, { key: 'z' });
      expect(canvasNodes()).toHaveLength(3);

      fireEvent.keyDown(window, { key: 'Z', metaKey: true });
      await waitFor(() => expect(canvasNodes()).toHaveLength(2));
    });

    it('rolls the canvas back to the last save when a save is refused, and says why', async () => {
      const { modelApi } = await import('../api/modelApi');
      await openReadingsArrive();
      placeTheIntakeService();
      await waitFor(() => expect(canvasNodes()).toHaveLength(3));
      vi.mocked(modelApi.applyFragment).mockRejectedValueOnce(new Error('The broker refused the fragment'));

      fireEvent.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() => expect(screen.getByText('The broker refused the fragment')).toBeInTheDocument());
      expect(canvasNodes()).toHaveLength(2);
      expect(screen.getByText('saved')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Undo' })).toBeDisabled();
    });

    it('keeps the drawing, its unsaved mark and its undo history when the first save of a new pipeline is refused', async () => {
      const { modelApi } = await import('../api/modelApi');
      await openReadingsArrive();
      fireEvent.click(screen.getByRole('button', { name: 'New' }));
      placeTheIntakeService();
      await waitFor(() => expect(canvasNodes()).toHaveLength(1));
      vi.mocked(modelApi.applyFragment).mockRejectedValueOnce(new Error('The broker refused the fragment'));

      fireEvent.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() => expect(screen.getByText('The broker refused the fragment')).toBeInTheDocument());
      expect(canvasNodes()).toHaveLength(1);
      expect(screen.getByText('unsaved changes')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Undo' })).toBeEnabled();
    });

    it('says a save failed when the refusal carries no message', async () => {
      const { modelApi } = await import('../api/modelApi');
      await openReadingsArrive();
      placeTheIntakeService();
      vi.mocked(modelApi.applyFragment).mockRejectedValueOnce('refused');

      fireEvent.click(screen.getByRole('button', { name: 'Save' }));

      await waitFor(() => expect(screen.getByText('Save failed.')).toBeInTheDocument());
    });

    it('offers a box for each run parameter a node is bound to, and runs with what was typed, read as JSON when it is', async () => {
      const { pipelineApi } = await import('../api/pipelineApi');
      vi.mocked(pipelineApi.spawnAsync).mockResolvedValueOnce({ runId: 'run-from-the-page' } as never);
      const { id } = catalystFixture();
      await openReadingsArrive();
      placeTheIntakeService();
      fireEvent.click(canvasNode(/intake/));
      fireEvent.change(screen.getByPlaceholderText('from param…'), { target: { value: ' rhythms ' } });
      fireEvent.click(screen.getByRole('button', { name: 'Save' }));
      await waitFor(() => expect(screen.getByText('saved')).toBeInTheDocument());

      const parameter = within(screen.getByText('rhythms').closest('label')!).getByRole('textbox');
      fireEvent.change(parameter, { target: { value: '["daily","hourly"]' } });
      fireEvent.click(screen.getByRole('button', { name: 'Run' }));

      await waitFor(() => expect(vi.mocked(pipelineApi.spawnAsync)).toHaveBeenCalledWith(id.readingsArrive, { rhythms: ['daily', 'hourly'] }));
      expect(await screen.findByText('Run starting…')).toBeInTheDocument();
      expect(screen.getByText('intake: pending')).toBeInTheDocument();
    });

    it('drops a binding whose parameter is cleared', async () => {
      await openReadingsArrive();
      placeTheIntakeService();
      fireEvent.click(canvasNode(/intake/));
      fireEvent.change(screen.getByPlaceholderText('from param…'), { target: { value: 'rhythms' } });
      expect(screen.getByText('rhythms')).toBeInTheDocument();

      fireEvent.change(screen.getByPlaceholderText('from param…'), { target: { value: '  ' } });

      expect(screen.queryByText('Params')).toBeNull();
    });

    it('says why a run could not start', async () => {
      const { pipelineApi } = await import('../api/pipelineApi');
      await openReadingsArrive();
      vi.mocked(pipelineApi.spawnAsync).mockRejectedValueOnce(new Error('No orchestrator is running'));
      fireEvent.click(screen.getByRole('button', { name: 'Run' }));
      await waitFor(() => expect(screen.getByText('No orchestrator is running')).toBeInTheDocument());

      vi.mocked(pipelineApi.spawnAsync).mockRejectedValueOnce('refused');
      fireEvent.click(screen.getByRole('button', { name: 'Run' }));
      await waitFor(() => expect(screen.getByText('Run failed.')).toBeInTheDocument());
    });

    it('opens a node in the inspector and closes it with the close button or a click on the empty canvas', async () => {
      await openReadingsArrive();
      fireEvent.click(canvasNode(/The answer/));
      expect(screen.getByRole('button', { name: 'Close inspector' })).toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: 'Close inspector' }));
      expect(screen.queryByRole('button', { name: 'Close inspector' })).toBeNull();

      fireEvent.click(canvasNode(/The answer/));
      fireEvent.click(document.querySelector('.react-flow__pane')!);
      expect(screen.queryByRole('button', { name: 'Close inspector' })).toBeNull();
    });

    it('places a start by hand with a payload port, and one standing for a state with a subject port', async () => {
      await openReadingsArrive();
      fireEvent.click(screen.getByRole('button', { name: 'Place a start by hand' }));
      await waitFor(() => expect(canvasNodes()).toHaveLength(3));
      expect(within(canvasNodes().at(-1)!).getByText('Input')).toBeInTheDocument();
      expect(within(canvasNodes().at(-1)!).getByText('payload')).toBeInTheDocument();

      fireEvent.click(screen.getAllByRole('button', { name: /Place a start node standing for .*running low/ })[0]);
      await waitFor(() => expect(canvasNodes()).toHaveLength(4));
      expect(within(canvasNodes().at(-1)!).getByText('subject')).toBeInTheDocument();
    });

    it('places an Output node from the answer row', async () => {
      await openReadingsArrive();
      fireEvent.click(screen.getByRole('button', { name: /Place an end node standing for/ }));
      await waitFor(() => expect(canvasNodes()).toHaveLength(3));
      expect(within(canvasNodes().at(-1)!).getByText('Output')).toBeInTheDocument();
      expect(within(canvasNodes().at(-1)!).getByText('value')).toBeInTheDocument();
    });

    it('says why a state could not be handed to the orchestrator', async () => {
      const { relationshipApi } = await import('../api/relationshipApi');
      vi.mocked(relationshipApi.create).mockRejectedValueOnce(new Error('The broker refused the relationship'));
      vi.mocked(relationshipApi.remove).mockRejectedValueOnce(new Error('The broker refused the relationship'));
      await openReadingsArrive();

      fireEvent.click(screen.getAllByRole('button', { name: /with the orchestrator/ })[0]);

      await waitFor(() => expect(screen.getByText('The broker refused the relationship')).toBeInTheDocument());
    });

    it('says a hand-over failed when the refusal carries no message', async () => {
      const { relationshipApi } = await import('../api/relationshipApi');
      vi.mocked(relationshipApi.create).mockRejectedValueOnce('refused');
      vi.mocked(relationshipApi.remove).mockRejectedValueOnce('refused');
      await openReadingsArrive();

      fireEvent.click(screen.getAllByRole('button', { name: /with the orchestrator/ })[0]);

      await waitFor(() => expect(screen.getByText('The state could not be handed to the orchestrator')).toBeInTheDocument());
    });

    it('renames the pipeline started by a follow-on node and adds a port it passes on', async () => {
      await openReadingsArrive();
      fireEvent.click(screen.getByRole('button', { name: 'Place a follow-on node starting Refill' }));
      await waitFor(() => expect(canvasNodes()).toHaveLength(3));
      fireEvent.click(canvasNodes().at(-1)!);

      fireEvent.change(screen.getByRole('combobox', { name: 'Starts' }), { target: { value: catalystFixture().id.storeSurplus } });
      fireEvent.click(screen.getByText('+ add port'));

      expect(screen.getByRole('combobox', { name: 'Starts' })).toHaveValue(catalystFixture().id.storeSurplus);
      expect(screen.getByRole('textbox', { name: 'Port 2 name' })).toHaveValue('port2');
      expect(screen.getByText('unsaved changes')).toBeInTheDocument();
    });

    it('starts an empty canvas from New, and takes a new name from the name box', async () => {
      await openReadingsArrive();
      fireEvent.click(screen.getByRole('button', { name: 'New' }));
      expect(canvasNodes()).toHaveLength(0);
      expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('New Pipeline');
      expect(screen.getByText('Start a new pipeline')).toBeInTheDocument();

      fireEvent.change(screen.getByRole('textbox', { name: 'Pipeline name' }), { target: { value: 'Overflow' } });
      expect(screen.getByRole('textbox', { name: 'Pipeline name' })).toHaveValue('Overflow');
    });

    it('says there are no services when the model holds no connection', () => {
      useModelStore.setState({ things: [], relationships: [], loaded: true });
      render(<MemoryRouter><PipelinePage /></MemoryRouter>);
      expect(screen.getByText('No services available')).toBeInTheDocument();
    });

    it('says why a run could not be cancelled', async () => {
      const { stopRun } = await import('../pipeline/stop');
      const { things, relationships, id } = catalystFixture();
      const relate = (subject: string, predicate: string, target: string) =>
        relationships.push({ Id: `${subject}-${predicate}-${target}`, Name: '', SubjectId: subject, PredicateId: predicate, TargetId: target, Properties: {} });
      things.push(
        { Id: 'of', Name: 'of', Properties: {} },
        { Id: 'run-kind', Name: 'Execution', IsArchetype: true, Properties: {} },
        { Id: ROLE.PipelineRun, Name: 'pipelineRun role', Properties: {} },
        { Id: 'held-up', Name: 'run 3', Properties: { status: 'waiting', startedUtc: '2026-10-15T09:00:00Z' } },
      );
      relate(ROLE.PipelineRun, id.binds, 'run-kind');
      relate('held-up', id.is, 'run-kind');
      relate('held-up', 'of', id.digest);
      useModelStore.setState({ things, relationships, loaded: true });
      render(<MemoryRouter><PipelinePage /></MemoryRouter>);
      fireEvent.change(await screen.findByRole('combobox', { name: 'Run history' }), { target: { value: 'held-up' } });

      vi.mocked(stopRun).mockRejectedValueOnce(new Error('The run had already ended'));
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.getByText('The run had already ended')).toBeInTheDocument());
      expect(vi.mocked(stopRun)).toHaveBeenCalledWith('held-up', 'Cancelled from the Pipelines page.', expect.anything());

      vi.mocked(stopRun).mockRejectedValueOnce('refused');
      fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
      await waitFor(() => expect(screen.getByText('Cancel failed.')).toBeInTheDocument());
    });
  });
});
