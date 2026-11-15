import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, render, screen, fireEvent, waitFor, within } from '@testing-library/react';

vi.mock('../hooks/useSse', () => ({
  SUBSCRIPTION_OPENED: 'SubscriptionOpened',
  useSubscription: () => {},
  useSse: () => ({ connection: 'live', on: () => () => {} }),
  addToSubscription: vi.fn().mockResolvedValue(null),
  removeFromSubscription: vi.fn().mockResolvedValue(undefined),
}));
vi.mock('../api/thingApi', () => ({
  thingApi: { getEffectiveProperties: vi.fn(), setProperty: vi.fn(), deleteProperty: vi.fn() },
}));
vi.mock('../api/callSettingsApi', () => ({ callSettingsApi: { get: vi.fn() } }));
vi.mock('../api/modelApi', () => ({ modelApi: { applyFragment: vi.fn() } }));
const account: { role: string | null } = { role: 'editor' };
vi.mock('../hooks/useAuthentication', () => ({ useAuthentication: () => account }));

import { useModelStore } from '../stores/modelStore';
import { useToastStore } from '../components/common/toastStore';
import { thingApi } from '../api/thingApi';
import { callSettingsApi, type CallSettings } from '../api/callSettingsApi';
import { modelApi } from '../api/modelApi';
import { ApiError } from '../api/client';
import { connectionSettingsFixture } from '../pipeline/connectionSettings.test.fixture';
import { installResizeObserverDouble } from '../testResizeObserver';
import { PipelinePage } from './PipelinePage';

const OPEN_THE_SETTINGS = 'Show the call settings of Catchment · OutOfDate';

type Fixture = ReturnType<typeof connectionSettingsFixture>;

function openTheSettings(adjust: (fixture: Fixture) => void = () => {}) {
  const fixture = connectionSettingsFixture();
  adjust(fixture);
  vi.mocked(thingApi.getEffectiveProperties).mockResolvedValue(fixture.resolved);
  vi.mocked(callSettingsApi.get).mockResolvedValue(fixture.callSettings);
  useModelStore.setState({ things: fixture.things, relationships: fixture.relationships, loaded: true });
  render(<PipelinePage />);
  fireEvent.click(screen.getByRole('button', { name: OPEN_THE_SETTINGS }));
  return { ...fixture, panel: screen.getByRole('region', { name: 'Call settings of Estimate-Rainfall' }) };
}

const line = (panel: HTMLElement, name: string) => within(panel).getByRole('listitem', { name });
const choiceOf = (panel: HTMLElement) => within(line(panel, 'completesWhen')).getByRole('combobox', { name: 'When the call is finished' });

function answerAgain(settings: CallSettings, fixture: Fixture) {
  vi.mocked(callSettingsApi.get).mockResolvedValue({ ...fixture.callSettings, ...settings });
}

describe('the call settings of the connection behind a state', () => {
  let restore: () => void;
  beforeEach(() => {
    restore = installResizeObserverDouble().restore;
    account.role = 'editor';
    useToastStore.setState({ toasts: [] });
    vi.mocked(thingApi.getEffectiveProperties).mockReset();
    vi.mocked(callSettingsApi.get).mockReset();
    vi.mocked(modelApi.applyFragment).mockReset().mockResolvedValue({ thingsCreated: 0, thingsUpdated: 0, relationshipsCreated: 1, things: [] });
    vi.mocked(thingApi.setProperty).mockReset().mockResolvedValue({ Id: '', Name: '', Properties: {} });
    vi.mocked(thingApi.deleteProperty).mockReset().mockResolvedValue({ message: 'Deleted' });
  });
  afterEach(() => restore());

  it('shows every setting the platform answers, each with where its value comes from', async () => {
    const { panel, id } = openTheSettings();

    await waitFor(() => expect(within(line(panel, 'callsAgainAtMost')).getByRole('spinbutton')).toHaveValue(5));
    expect(callSettingsApi.get).toHaveBeenCalledWith(id.connection, expect.any(AbortSignal));
    expect(within(panel).getAllByRole('listitem').map((item) => item.getAttribute('aria-label'))).toEqual([
      'completesWhen', 'callsAgainWhileItsThingStaysIn', 'doesNotCallAgainForSeconds', 'callsAgainForNoLongerThanSeconds', 'callsAgainAtMost',
    ]);
    expect(line(panel, 'completesWhen')).toHaveTextContent('set on rainfall-estimator prototype, the archetype of its service');
    expect(line(panel, 'callsAgainForNoLongerThanSeconds')).toHaveTextContent('set on rainfall-estimator prototype, the archetype of its service');
    expect(within(line(panel, 'callsAgainForNoLongerThanSeconds')).getByRole('spinbutton')).toHaveValue(900);
    for (const name of ['callsAgainWhileItsThingStaysIn', 'doesNotCallAgainForSeconds', 'callsAgainAtMost'])
      expect(line(panel, name)).toHaveTextContent("the platform's built-in value");
  });

  it('lists the choices of when the call is finished from the model, in the words of the page, with the chosen one selected', async () => {
    const { panel, id } = openTheSettings();

    await waitFor(() => expect(choiceOf(panel)).toHaveValue(id.whenTheServiceReportsCompletion));
    expect(within(choiceOf(panel)).getAllByRole('option').map((option) => option.textContent)).toEqual([
      'When the service answers', 'When the Thing reaches its done state', 'whenTheServiceReportsCompletion',
    ]);
  });

  it('names the service a choice is inherited from', async () => {
    const { panel } = openTheSettings((fixture) => {
      fixture.callSettings.completesWhen = { value: fixture.id.whenTheServiceAnswers, source: 'service', from: { id: fixture.id.service, name: 'rainfall-estimator' } };
    });

    await waitFor(() => expect(line(panel, 'completesWhen')).toHaveTextContent('set on its service rainfall-estimator'));
  });

  it('writes the chosen member on the connection as one fragment, then reads the settings again', async () => {
    const { panel, id } = openTheSettings();
    await waitFor(() => expect(choiceOf(panel)).toHaveValue(id.whenTheServiceReportsCompletion));

    fireEvent.change(choiceOf(panel), { target: { value: id.whenTheServiceAnswers } });

    await waitFor(() => expect(modelApi.applyFragment).toHaveBeenCalledTimes(1));
    expect(JSON.parse(vi.mocked(modelApi.applyFragment).mock.calls[0][0])).toEqual({
      Name: 'Estimate-Rainfall completesWhen whenTheServiceAnswers',
      Relationships: [{ Subject: id.connection, Predicate: id.completesWhen, Target: id.whenTheServiceAnswers }],
    });
    answerAgain({ completesWhen: { value: id.whenTheServiceAnswers, source: 'connection', from: { id: id.connection, name: 'Estimate-Rainfall' } } }, connectionSettingsFixture());
    act(() => useModelStore.getState().applyBatch({
      relationshipUpserts: [{ Id: 'chosen', Name: '', SubjectId: id.connection, PredicateId: id.completesWhen, TargetId: id.whenTheServiceAnswers, Properties: {} }],
    }));
    await waitFor(() => expect(line(panel, 'completesWhen')).toHaveTextContent('set on this connection'));
    expect(choiceOf(panel)).toHaveValue(id.whenTheServiceAnswers);
  });

  it('retracts the connection\'s own choice in the same fragment that writes the new one', async () => {
    const { panel, id } = openTheSettings((fixture) => {
      fixture.relationships.push({ Id: 'own choice', Name: '', SubjectId: fixture.id.connection, PredicateId: fixture.id.completesWhen, TargetId: fixture.id.whenTheServiceAnswers, Properties: {} });
      fixture.callSettings.completesWhen = { value: fixture.id.whenTheServiceAnswers, source: 'connection', from: { id: fixture.id.connection, name: 'Estimate-Rainfall' } };
    });
    await waitFor(() => expect(choiceOf(panel)).toHaveValue(id.whenTheServiceAnswers));

    fireEvent.change(choiceOf(panel), { target: { value: id.whenTheThingReachesTheDoneState } });

    await waitFor(() => expect(modelApi.applyFragment).toHaveBeenCalledTimes(1));
    expect(JSON.parse(vi.mocked(modelApi.applyFragment).mock.calls[0][0])).toEqual({
      Name: 'Estimate-Rainfall completesWhen whenTheThingReachesTheDoneState',
      Retractions: [{ Id: 'own choice' }],
      Relationships: [{ Subject: id.connection, Predicate: id.completesWhen, Target: id.whenTheThingReachesTheDoneState }],
    });
  });

  it('says in the platform\'s words why a choice was refused, and keeps the choice it shows', async () => {
    const { panel, id } = openTheSettings();
    await waitFor(() => expect(choiceOf(panel)).toHaveValue(id.whenTheServiceReportsCompletion));
    vi.mocked(modelApi.applyFragment).mockRejectedValue(new ApiError(409, '{"error":"a connection has one completion choice; remove the current one first"}'));

    fireEvent.change(choiceOf(panel), { target: { value: id.whenTheServiceAnswers } });

    await waitFor(() => expect(useToastStore.getState().toasts).toContainEqual(expect.objectContaining({
      type: 'error', message: 'The choice was not written: a connection has one completion choice; remove the current one first',
    })));
    expect(choiceOf(panel)).toHaveValue(id.whenTheServiceReportsCompletion);
  });

  it('writes a changed value on the connection, then reads the settings again and says it is set on this connection', async () => {
    const { panel, id, callSettings } = openTheSettings();
    const field = await waitFor(() => within(line(panel, 'callsAgainForNoLongerThanSeconds')).getByRole('spinbutton'));

    fireEvent.change(field, { target: { value: '120' } });
    fireEvent.keyDown(field, { key: 'Enter' });

    await waitFor(() => expect(thingApi.setProperty).toHaveBeenCalledWith(id.connection, 'callsAgainForNoLongerThanSeconds', 'vos.Integer', '120'));
    vi.mocked(callSettingsApi.get).mockResolvedValue({
      ...callSettings,
      callsAgainForNoLongerThanSeconds: { value: 120, source: 'connection', from: { id: id.connection, name: 'Estimate-Rainfall' } },
    });
    act(() => useModelStore.getState().applyBatch({ thingPropertyUpdates: [{ id: id.connection, path: 'callsAgainForNoLongerThanSeconds', value: 120 }] }));
    await waitFor(() => expect(line(panel, 'callsAgainForNoLongerThanSeconds')).toHaveTextContent('set on this connection'));
    expect(within(line(panel, 'callsAgainForNoLongerThanSeconds')).getByRole('spinbutton')).toHaveValue(120);
    expect(callSettingsApi.get).toHaveBeenCalledTimes(2);
  });

  it('removes a value set on the connection, so the one from its service or the platform applies', async () => {
    const { panel, id } = openTheSettings((fixture) => {
      fixture.callSettings.callsAgainAtMost = { value: 2, source: 'connection', from: { id: fixture.id.connection, name: 'Estimate-Rainfall' } };
    });
    await waitFor(() => expect(within(line(panel, 'callsAgainAtMost')).getByRole('spinbutton')).toHaveValue(2));
    expect(within(line(panel, 'doesNotCallAgainForSeconds')).queryByRole('button', { name: /Remove/ })).toBeNull();

    fireEvent.click(within(line(panel, 'callsAgainAtMost')).getByRole('button', { name: 'Remove callsAgainAtMost from this connection' }));

    await waitFor(() => expect(thingApi.deleteProperty).toHaveBeenCalledWith(id.connection, 'callsAgainAtMost'));
    expect(useToastStore.getState().toasts).toContainEqual(expect.objectContaining({ type: 'success', message: 'callsAgainAtMost is no longer set on this connection' }));
  });

  it('says a removal the platform refuses', async () => {
    const { panel } = openTheSettings((fixture) => {
      fixture.callSettings.callsAgainAtMost = { value: 2, source: 'connection', from: { id: fixture.id.connection, name: 'Estimate-Rainfall' } };
    });
    vi.mocked(thingApi.deleteProperty).mockRejectedValue(new Error('Property not found: callsAgainAtMost'));
    const remove = await waitFor(() => within(line(panel, 'callsAgainAtMost')).getByRole('button', { name: 'Remove callsAgainAtMost from this connection' }));

    fireEvent.click(remove);

    await waitFor(() => expect(useToastStore.getState().toasts).toContainEqual(expect.objectContaining({ type: 'error', message: 'Property not found: callsAgainAtMost' })));
  });

  it('says when the removal fails without a reason', async () => {
    const { panel } = openTheSettings((fixture) => {
      fixture.callSettings.callsAgainAtMost = { value: 2, source: 'connection', from: { id: fixture.id.connection, name: 'Estimate-Rainfall' } };
    });
    vi.mocked(thingApi.deleteProperty).mockRejectedValue('refused');
    const remove = await waitFor(() => within(line(panel, 'callsAgainAtMost')).getByRole('button', { name: 'Remove callsAgainAtMost from this connection' }));

    fireEvent.click(remove);

    await waitFor(() => expect(useToastStore.getState().toasts).toContainEqual(expect.objectContaining({
      type: 'error', message: 'callsAgainAtMost could not be removed from this connection',
    })));
  });

  it('shows a user who may not change the model every value and nothing to change it with', async () => {
    account.role = 'viewer';
    const { panel } = openTheSettings((fixture) => {
      fixture.callSettings.callsAgainAtMost = { value: 2, source: 'connection', from: { id: fixture.id.connection, name: 'Estimate-Rainfall' } };
    });

    await waitFor(() => expect(line(panel, 'completesWhen')).toHaveTextContent('whenTheServiceReportsCompletion'));
    expect(line(panel, 'callsAgainForNoLongerThanSeconds')).toHaveTextContent('900');
    expect(within(panel).queryAllByRole('combobox')).toEqual([]);
    expect(within(panel).queryAllByRole('spinbutton')).toEqual([]);
    expect(within(panel).queryAllByRole('checkbox')).toEqual([]);
    expect(within(panel).queryByRole('button', { name: /Remove/ })).toBeNull();
    expect(panel).toHaveTextContent('You may read these settings but not change them');
  });

  it('says when the platform gives no type for a value, and offers nothing to change it with', async () => {
    const { panel } = openTheSettings((fixture) => {
      delete fixture.resolved['ServiceBinding.callsAgainAtMost'];
    });

    await waitFor(() => expect(line(panel, 'callsAgainAtMost')).toHaveTextContent('The platform gives no type for this value, so it cannot be changed here'));
    expect(within(line(panel, 'callsAgainAtMost')).queryByRole('spinbutton')).toBeNull();
    expect(within(line(panel, 'doesNotCallAgainForSeconds')).getByRole('spinbutton')).toHaveValue(10);
  });

  it('says when the types cannot be read, and offers no value to change', async () => {
    const fixture = connectionSettingsFixture();
    vi.mocked(thingApi.getEffectiveProperties).mockRejectedValue(new Error('refused'));
    vi.mocked(callSettingsApi.get).mockResolvedValue(fixture.callSettings);
    useModelStore.setState({ things: fixture.things, relationships: fixture.relationships, loaded: true });
    render(<PipelinePage />);
    fireEvent.click(screen.getByRole('button', { name: OPEN_THE_SETTINGS }));
    const panel = screen.getByRole('region', { name: 'Call settings of Estimate-Rainfall' });

    await waitFor(() => expect(useToastStore.getState().toasts).toContainEqual(expect.objectContaining({
      type: 'error', message: 'The types of these settings could not be read, so they cannot be changed',
    })));
    expect(line(panel, 'callsAgainAtMost')).toHaveTextContent('5');
    expect(within(panel).queryAllByRole('spinbutton')).toEqual([]);
  });

  it('says in the platform\'s words when the settings cannot be read', async () => {
    const fixture = connectionSettingsFixture();
    vi.mocked(thingApi.getEffectiveProperties).mockResolvedValue(fixture.resolved);
    vi.mocked(callSettingsApi.get).mockRejectedValue(new ApiError(404, '{"error":"no connection Estimate-Rainfall"}'));
    useModelStore.setState({ things: fixture.things, relationships: fixture.relationships, loaded: true });
    render(<PipelinePage />);
    fireEvent.click(screen.getByRole('button', { name: OPEN_THE_SETTINGS }));
    const panel = screen.getByRole('region', { name: 'Call settings of Estimate-Rainfall' });

    await waitFor(() => expect(panel).toHaveTextContent('The call settings could not be read: no connection Estimate-Rainfall'));
    expect(within(panel).queryAllByRole('listitem')).toEqual([]);
  });

  it('says it is reading until the platform answers', () => {
    const fixture = connectionSettingsFixture();
    vi.mocked(thingApi.getEffectiveProperties).mockResolvedValue(fixture.resolved);
    vi.mocked(callSettingsApi.get).mockReturnValue(new Promise(() => {}));
    useModelStore.setState({ things: fixture.things, relationships: fixture.relationships, loaded: true });
    render(<PipelinePage />);
    fireEvent.click(screen.getByRole('button', { name: OPEN_THE_SETTINGS }));

    expect(screen.getByRole('region', { name: 'Call settings of Estimate-Rainfall' })).toHaveTextContent('Reading the call settings');
  });

  it('closes when the connection leaves the model', async () => {
    const { id } = openTheSettings();

    act(() => useModelStore.getState().applyBatch({ thingRemovals: [id.connection] }));

    expect(screen.queryByRole('region', { name: /^Call settings of/ })).toBeNull();
  });

  it('closes', () => {
    const { panel } = openTheSettings();

    fireEvent.click(within(panel).getByRole('button', { name: 'Close inspector' }));

    expect(screen.queryByRole('region', { name: 'Call settings of Estimate-Rainfall' })).toBeNull();
  });
});
