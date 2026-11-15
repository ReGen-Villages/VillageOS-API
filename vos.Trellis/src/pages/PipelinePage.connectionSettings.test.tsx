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
const account: { role: string | null } = { role: 'editor' };
vi.mock('../hooks/useAuthentication', () => ({ useAuthentication: () => account }));

import { useModelStore } from '../stores/modelStore';
import { useToastStore } from '../components/common/toastStore';
import { thingApi } from '../api/thingApi';
import { connectionSettingsFixture } from '../pipeline/connectionSettings.test.fixture';
import { installResizeObserverDouble } from '../testResizeObserver';
import { PipelinePage } from './PipelinePage';

const OPEN_THE_SETTINGS = 'Show the call settings of Catchment · OutOfDate';

function openTheSettings() {
  const fixture = connectionSettingsFixture();
  vi.mocked(thingApi.getEffectiveProperties).mockResolvedValue(fixture.resolved);
  useModelStore.setState({ things: fixture.things, relationships: fixture.relationships, loaded: true });
  render(<PipelinePage />);
  fireEvent.click(screen.getByRole('button', { name: OPEN_THE_SETTINGS }));
  return { ...fixture, panel: screen.getByRole('region', { name: 'Call settings of Estimate-Rainfall' }) };
}

const line = (panel: HTMLElement, name: string) => within(panel).getByRole('listitem', { name });

describe('the call settings of the connection behind a state', () => {
  let restore: () => void;
  beforeEach(() => {
    restore = installResizeObserverDouble().restore;
    account.role = 'editor';
    useToastStore.setState({ toasts: [] });
    vi.mocked(thingApi.getEffectiveProperties).mockReset();
    vi.mocked(thingApi.setProperty).mockReset().mockResolvedValue({ Id: '', Name: '', Properties: {} });
    vi.mocked(thingApi.deleteProperty).mockReset().mockResolvedValue({ message: 'Deleted' });
  });
  afterEach(() => restore());

  it('lists every setting a connection inherits, each with the archetype it comes from', async () => {
    const { panel } = openTheSettings();

    await waitFor(() => expect(within(line(panel, 'callsAgainAtMost')).getByRole('spinbutton')).toHaveValue(5));
    expect(within(panel).getAllByRole('listitem').map((item) => item.getAttribute('aria-label'))).toEqual([
      'callsAgainAtMost', 'callsAgainForNoLongerThanSeconds', 'callsAgainWhileItsThingStaysIn', 'completion',
      'doesNotCallAgainForSeconds', 'done_within', 'waitsForTheHandler',
    ]);
    for (const name of ['callsAgainAtMost', 'callsAgainForNoLongerThanSeconds', 'callsAgainWhileItsThingStaysIn', 'doesNotCallAgainForSeconds', 'waitsForTheHandler'])
      expect(line(panel, name)).toHaveTextContent('inherited from ServiceBinding');
    for (const name of ['completion', 'done_within'])
      expect(line(panel, name)).toHaveTextContent('inherited from SiteAnalysisConnection');
  });

  it('enters each value by its declared type: true or false, a whole number, or text', async () => {
    const { panel } = openTheSettings();

    await waitFor(() => expect(within(line(panel, 'waitsForTheHandler')).getByRole('checkbox')).not.toBeChecked());
    expect(within(line(panel, 'callsAgainWhileItsThingStaysIn')).getByRole('checkbox')).toBeChecked();
    expect(within(line(panel, 'done_within')).getByRole('spinbutton')).toHaveValue(300);
    expect(within(line(panel, 'completion')).getByRole('textbox')).toHaveValue('ack');
  });

  it('writes a changed value on the connection, and then says it is set on this connection', async () => {
    const { panel, id } = openTheSettings();
    const field = await waitFor(() => within(line(panel, 'callsAgainForNoLongerThanSeconds')).getByRole('spinbutton'));

    fireEvent.change(field, { target: { value: '120' } });
    fireEvent.keyDown(field, { key: 'Enter' });

    await waitFor(() => expect(thingApi.setProperty).toHaveBeenCalledWith(id.connection, 'callsAgainForNoLongerThanSeconds', 'vos.Integer', '120'));
    act(() => useModelStore.getState().applyBatch({ thingPropertyUpdates: [{ id: id.connection, path: 'callsAgainForNoLongerThanSeconds', value: 120 }] }));
    expect(line(panel, 'callsAgainForNoLongerThanSeconds')).toHaveTextContent('set on this connection');
    expect(within(line(panel, 'callsAgainForNoLongerThanSeconds')).getByRole('spinbutton')).toHaveValue(120);
  });

  it('removes a value set on the connection, and then shows the inherited value again', async () => {
    const { panel, id } = openTheSettings();
    act(() => useModelStore.getState().applyBatch({ thingPropertyUpdates: [{ id: id.connection, path: 'callsAgainForNoLongerThanSeconds', value: 120 }] }));
    await waitFor(() => expect(within(line(panel, 'callsAgainForNoLongerThanSeconds')).getByRole('spinbutton')).toHaveValue(120));
    expect(within(line(panel, 'callsAgainAtMost')).queryByRole('button', { name: /inherited/ })).toBeNull();

    fireEvent.click(within(line(panel, 'callsAgainForNoLongerThanSeconds')).getByRole('button', { name: 'Use the value inherited from ServiceBinding' }));

    await waitFor(() => expect(thingApi.deleteProperty).toHaveBeenCalledWith(id.connection, 'callsAgainForNoLongerThanSeconds'));
    act(() => useModelStore.getState().applyBatch({ thingPropertyRemovals: [{ id: id.connection, path: 'callsAgainForNoLongerThanSeconds' }] }));
    expect(line(panel, 'callsAgainForNoLongerThanSeconds')).toHaveTextContent('inherited from ServiceBinding');
    expect(within(line(panel, 'callsAgainForNoLongerThanSeconds')).getByRole('spinbutton')).toHaveValue(60);
  });

  it('shows a property newly declared on the archetype, with no change to the console', async () => {
    const { panel, id, resolved } = openTheSettings();
    await waitFor(() => expect(within(line(panel, 'callsAgainAtMost')).getByRole('spinbutton')).toHaveValue(5));
    vi.mocked(thingApi.getEffectiveProperties).mockResolvedValue({
      ...resolved,
      'SiteAnalysisConnection.ServiceBinding.startsAfterSeconds': { Value: 0, Type: 'vos.Integer', IsInherited: true, InheritedFrom: id.serviceBinding },
    });

    act(() => useModelStore.getState().applyBatch({ thingPropertyUpdates: [{ id: id.serviceBinding, path: 'startsAfterSeconds', value: 0 }] }));

    expect(line(panel, 'startsAfterSeconds')).toHaveTextContent('inherited from ServiceBinding');
    await waitFor(() => expect(within(line(panel, 'startsAfterSeconds')).getByRole('spinbutton')).toHaveValue(0));
    expect(thingApi.getEffectiveProperties).toHaveBeenCalledTimes(2);
  });

  it('shows a user who may not change the model every value and nothing to change it with', async () => {
    account.role = 'viewer';
    const { panel, id } = openTheSettings();
    act(() => useModelStore.getState().applyBatch({ thingPropertyUpdates: [{ id: id.connection, path: 'callsAgainForNoLongerThanSeconds', value: 120 }] }));

    await waitFor(() => expect(line(panel, 'completion')).toHaveTextContent('ack'));
    expect(line(panel, 'callsAgainForNoLongerThanSeconds')).toHaveTextContent('120');
    expect(within(panel).queryAllByRole('textbox')).toEqual([]);
    expect(within(panel).queryAllByRole('spinbutton')).toEqual([]);
    expect(within(panel).queryAllByRole('checkbox')).toEqual([]);
    expect(within(panel).queryByRole('button', { name: /inherited/ })).toBeNull();
    expect(panel).toHaveTextContent('You may read these settings but not change them');
  });

  it('says a removal the platform refuses, and keeps the value set on the connection', async () => {
    const { panel, id } = openTheSettings();
    act(() => useModelStore.getState().applyBatch({ thingPropertyUpdates: [{ id: id.connection, path: 'callsAgainAtMost', value: 2 }] }));
    vi.mocked(thingApi.deleteProperty).mockRejectedValue(new Error('Property not found: callsAgainAtMost'));

    fireEvent.click(within(line(panel, 'callsAgainAtMost')).getByRole('button', { name: 'Use the value inherited from ServiceBinding' }));

    await waitFor(() => expect(useToastStore.getState().toasts).toContainEqual(expect.objectContaining({ type: 'error', message: 'Property not found: callsAgainAtMost' })));
    expect(line(panel, 'callsAgainAtMost')).toHaveTextContent('set on this connection');
  });

  it('says when the platform gives no type for a value, and offers nothing to change it with', async () => {
    const fixture = connectionSettingsFixture();
    const { 'SiteAnalysisConnection.completion': _completion, ...withoutCompletion } = fixture.resolved;
    vi.mocked(thingApi.getEffectiveProperties).mockResolvedValue(withoutCompletion);
    useModelStore.setState({ things: fixture.things, relationships: fixture.relationships, loaded: true });
    render(<PipelinePage />);
    fireEvent.click(screen.getByRole('button', { name: OPEN_THE_SETTINGS }));
    const panel = screen.getByRole('region', { name: 'Call settings of Estimate-Rainfall' });

    await waitFor(() => expect(line(panel, 'completion')).toHaveTextContent('The platform gives no type for this value, so it cannot be changed here'));
    expect(within(line(panel, 'completion')).queryByRole('textbox')).toBeNull();
    expect(within(line(panel, 'done_within')).getByRole('spinbutton')).toHaveValue(300);
  });

  it('says when the types cannot be read, and offers nothing to change', async () => {
    const fixture = connectionSettingsFixture();
    vi.mocked(thingApi.getEffectiveProperties).mockRejectedValue(new Error('refused'));
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

  it('closes', () => {
    const { panel } = openTheSettings();

    fireEvent.click(within(panel).getByRole('button', { name: 'Close inspector' }));

    expect(screen.queryByRole('region', { name: 'Call settings of Estimate-Rainfall' })).toBeNull();
  });
});
