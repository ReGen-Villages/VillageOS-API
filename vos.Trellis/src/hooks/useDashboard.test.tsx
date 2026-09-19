import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import type { Binding } from '../types/dashboard';
import type { ModelReads } from '../api/modelReads';
import type { BindingResult, ResolveContext } from '../api/dashboardApi';

const answered = vi.fn<(binding: Binding, ctx: ResolveContext) => Promise<BindingResult>>();
vi.mock('../api/dashboardApi', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/dashboardApi')>()),
  resolveBinding: (binding: Binding, ctx: ResolveContext) => answered(binding, ctx),
}));

const { useResolveContext, useBinding } = await import('./useDashboard');
const { buildModelIndex } = await import('../api/dashboardApi');
const { useUiStore } = await import('../stores/uiStore');

// Widgets each resolve their own bindings, so without something shared between them a page
// showing the same state in a count, a funnel stage and a table asks for it three times per
// refresh. What shares those reads is the ModelReads the context carries, so what this asserts
// is that one generation builds exactly one of them.
describe('useResolveContext', () => {
  const idx = buildModelIndex([], []);
  const reads = () => ({}) as ModelReads;

  it('gives every widget of one refresh generation the same reads', () => {
    const { result, rerender } = renderHook(({ nonce }) => useResolveContext(idx, null, 'Site', reads, nonce), {
      initialProps: { nonce: 1 },
    });
    const first = result.current.reads;
    expect(first).toBeDefined();
    rerender({ nonce: 1 });
    expect(result.current.reads).toBe(first);
  });

  it('starts a fresh set of reads when the generation moves on', () => {
    const { result, rerender } = renderHook(({ nonce }) => useResolveContext(idx, null, 'Site', reads, nonce), {
      initialProps: { nonce: 1 },
    });
    const first = result.current.reads;
    rerender({ nonce: 2 });
    expect(result.current.reads).not.toBe(first);
  });
});

// One open dashboard asked the platform tens of times a second on a running model, because every
// live event re-resolved every binding on the page.
describe('useBinding waits on what its answer is made of', () => {
  const reads = () => ({}) as ModelReads;
  const flaggedCount: Binding = { kind: 'stateCount', state: 'flagged' };
  const buildingsHeld: Binding = { kind: 'aggregate', archetype: 'Building', op: 'count' };
  const reservoirs: Binding = { kind: 'service', endpoint: '/api/endpoints/reservoirs' };

  const onThePage = (binding: Binding) =>
    renderHook(
      ({ model, events, cadence }) =>
        useBinding(binding, useResolveContext(model, null, 'Site', reads, events, cadence)),
      { initialProps: { model: buildModelIndex([], []), events: 0, cadence: 0 } },
    );

  beforeEach(() => {
    useUiStore.setState({ stateVersions: {} });
    answered.mockReset().mockResolvedValue(3);
  });

  it('asks again when the state it counts moves', async () => {
    const { result } = onThePage(flaggedCount);
    await waitFor(() => expect(result.current.value).toBe(3));

    act(() => useUiStore.getState().statesMoved(['flagged']));

    await waitFor(() => expect(answered).toHaveBeenCalledTimes(2));
  });

  it('stays put when a state it does not read moves', async () => {
    const { result } = onThePage(flaggedCount);
    await waitFor(() => expect(result.current.value).toBe(3));

    act(() => useUiStore.getState().statesMoved(['cleared']));
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });

    expect(answered).toHaveBeenCalledTimes(1);
  });

  it('stays put when a property changes elsewhere in the model', async () => {
    const { result, rerender } = onThePage(flaggedCount);
    await waitFor(() => expect(result.current.value).toBe(3));

    rerender({ model: buildModelIndex([], []), events: 1, cadence: 0 });
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });

    expect(answered).toHaveBeenCalledTimes(1);
  });

  it('asks again on the cadence the page states', async () => {
    const { result, rerender } = onThePage(flaggedCount);
    await waitFor(() => expect(result.current.value).toBe(3));

    rerender({ model: buildModelIndex([], []), events: 0, cadence: 1 });

    await waitFor(() => expect(answered).toHaveBeenCalledTimes(2));
  });

  it('follows the cadence alone for a figure a service answers', async () => {
    const { result, rerender } = onThePage(reservoirs);
    await waitFor(() => expect(result.current.value).toBe(3));

    act(() => useUiStore.getState().statesMoved(['flagged']));
    rerender({ model: buildModelIndex([], []), events: 1, cadence: 0 });
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });
    expect(answered).toHaveBeenCalledTimes(1);

    rerender({ model: buildModelIndex([], []), events: 1, cadence: 1 });
    await waitFor(() => expect(answered).toHaveBeenCalledTimes(2));
  });

  it('re-reads a figure the loaded model answers when that model is rebuilt, and not when a state moves', async () => {
    const { result, rerender } = onThePage(buildingsHeld);
    await waitFor(() => expect(result.current.value).toBe(3));

    act(() => useUiStore.getState().statesMoved(['flagged']));
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)); });
    expect(answered).toHaveBeenCalledTimes(1);

    rerender({ model: buildModelIndex([], []), events: 0, cadence: 0 });
    await waitFor(() => expect(answered).toHaveBeenCalledTimes(2));
  });

  it('resolves against the newest generation\'s reads when it does resolve', async () => {
    const { result, rerender } = onThePage(flaggedCount);
    await waitFor(() => expect(result.current.value).toBe(3));

    rerender({ model: buildModelIndex([], []), events: 1, cadence: 0 });
    rerender({ model: buildModelIndex([], []), events: 1, cadence: 1 });

    await waitFor(() => expect(answered).toHaveBeenCalledTimes(2));
    expect(answered.mock.calls[1][1].serverRefresh).toBe(1);
    expect(answered.mock.calls[1][1].nonce).toBe(1);
  });
});
