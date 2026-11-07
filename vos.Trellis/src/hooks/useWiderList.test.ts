import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { CoveredObjects } from '../types/subscription';
import type { VosRelationship, VosThing } from '../types/vos';

const handlers = new Map<string, (data: unknown) => void>();
vi.mock('./useSse', () => ({
  SUBSCRIPTION_OPENED: 'SubscriptionOpened',
  addToSubscription: vi.fn(),
  removeFromSubscription: vi.fn().mockResolvedValue(undefined),
  useSse: () => ({ connection: 'live', on }),
}));

/** Stable across renders, as the real hook's is; a new function each render would restart the widening on every render. */
function on(event: string, handler: (data: unknown) => void) {
  handlers.set(event, handler);
  return () => { handlers.delete(event); };
}

import { addToSubscription, removeFromSubscription } from './useSse';
import { useModelStore } from '../stores/modelStore';
import { useWiderList } from './useWiderList';

const thing = (Id: string, Name = Id): VosThing => ({ Id, Name, Properties: {} });
const relate = (Id: string, SubjectId: string, TargetId: string): VosRelationship =>
  ({ Id, Name: '', SubjectId, PredicateId: 'is', TargetId, Properties: {} });

const KIND = 'strategy-kind';
/** What the platform answers for the kind: the kind, its members, and the `is` relationships between them. */
const covered = (): CoveredObjects => ({
  things: [thing(KIND, 'Strategy'), thing('s1', 'Nearest first'), thing('s2', 'Cheapest first'), thing('s3', 'Farthest first')],
  relationships: [relate('s1-is', 's1', KIND), relate('s2-is', 's2', KIND), relate('s3-is', 's3', KIND)],
  thingStates: new Map([['s3', ['retired']]]),
});

const heldIds = () => useModelStore.getState().things.map((t) => t.Id).sort();
const heldRelationshipIds = () => useModelStore.getState().relationships.map((r) => r.Id).sort();
const removed = () => vi.mocked(removeFromSubscription).mock.calls.flatMap(([ids]) => ids).sort();

describe('useWiderList', () => {
  beforeEach(() => {
    handlers.clear();
    vi.mocked(addToSubscription).mockReset().mockResolvedValue(covered());
    vi.mocked(removeFromSubscription).mockClear();
    // The page already holds the kind, through an offered Thing's `is`, and one of its members.
    useModelStore.setState({ things: [thing('is'), thing(KIND, 'Strategy'), thing('s1', 'Nearest first')], relationships: [relate('s1-is', 's1', KIND)], loaded: true });
  });

  it('asks the subscription for the kinds and puts what it answers, with its states, in the store', async () => {
    renderHook(() => useWiderList([KIND], () => new Set()));

    await waitFor(() => expect(heldIds()).toEqual(['is', 's1', 's2', 's3', KIND]));
    expect(addToSubscription).toHaveBeenCalledWith({ typeIds: [KIND] });
    expect(heldRelationshipIds()).toEqual(['s1-is', 's2-is', 's3-is']);
    expect(useModelStore.getState().thingStates.get('s3')).toEqual(['retired']);
  });

  it('asks for nothing while no kind is named', () => {
    const { rerender } = renderHook(({ kinds }) => useWiderList(kinds, () => new Set()), { initialProps: { kinds: null as string[] | null } });
    rerender({ kinds: [] });

    expect(addToSubscription).not.toHaveBeenCalled();
  });

  // What the page held before the widening stays its own: the kind came with an offered Thing, and
  // a member the ports offer is read by the inspector whether or not the wider list is open.
  it('takes away what it brought in and nothing the page held, keeping what the drawing still refers to', async () => {
    const { unmount } = renderHook(() => useWiderList([KIND], () => new Set(['s3'])));
    await waitFor(() => expect(heldIds()).toContain('s2'));

    unmount();

    expect(heldIds()).toEqual(['is', 's1', 's3', KIND]);
    expect(heldRelationshipIds()).toEqual(['s1-is', 's3-is']);
    expect(removed()).toEqual(['s2', 's2-is']);
  });

  it('asks again when the subscription reopens, since the reopened one starts from the page\'s declaration', async () => {
    renderHook(() => useWiderList([KIND], () => new Set()));
    await waitFor(() => expect(addToSubscription).toHaveBeenCalledTimes(1));
    useModelStore.setState({ things: [thing('is'), thing(KIND, 'Strategy')], relationships: [] });

    act(() => handlers.get('SubscriptionOpened')?.({}));

    await waitFor(() => expect(addToSubscription).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(heldIds()).toEqual(['is', 's1', 's2', 's3', KIND]));
  });

  it('waits for a subscription to open when none is, and takes nothing away for an answer it never got', async () => {
    vi.mocked(addToSubscription).mockResolvedValueOnce(null);
    const { unmount } = renderHook(() => useWiderList([KIND], () => new Set()));
    await waitFor(() => expect(addToSubscription).toHaveBeenCalledTimes(1));
    expect(heldIds()).toEqual(['is', 's1', KIND]);

    act(() => handlers.get('SubscriptionOpened')?.({}));
    await waitFor(() => expect(heldIds()).toContain('s2'));
    unmount();
    expect(removed()).toEqual(['s2', 's2-is', 's3', 's3-is']);
  });

  it('gives back what arrives after the list closed, and keeps it out of the store', async () => {
    let answer: (value: CoveredObjects) => void = () => {};
    vi.mocked(addToSubscription).mockReturnValueOnce(new Promise((resolve) => { answer = resolve; }));
    const { unmount } = renderHook(() => useWiderList([KIND], () => new Set()));
    await waitFor(() => expect(addToSubscription).toHaveBeenCalled());

    unmount();
    answer(covered());

    await waitFor(() => expect(removed()).toEqual(['s2', 's2-is', 's3', 's3-is']));
    expect(heldIds()).toEqual(['is', 's1', KIND]);
  });

  it.each([
    ['an error', new Error('refused'), 'refused'],
    ['a bare value', 'refused outright', 'refused outright'],
  ])('reports a refused addition thrown as %s and leaves the store as it was', async (_, thrown, reported) => {
    vi.mocked(addToSubscription).mockRejectedValueOnce(thrown);
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
    const { unmount } = renderHook(() => useWiderList([KIND], () => new Set()));

    await waitFor(() => expect(warn).toHaveBeenCalledWith('The wider list could not be read:', reported));
    unmount();
    expect(heldIds()).toEqual(['is', 's1', KIND]);
    expect(removeFromSubscription).not.toHaveBeenCalled();
    warn.mockRestore();
  });
});
