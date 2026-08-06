import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useFlashTimer } from './useFlashTimer';
import { useUiStore } from '../stores/uiStore';

const FLASH_DURATION_MS = 500;

function flashedNodes(): string[] {
  return [...useUiStore.getState().flashingNodeIds];
}

function flashedEdges(): string[] {
  return [...useUiStore.getState().flashingEdgeIds];
}

describe('useFlashTimer', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    useUiStore.setState({ flashingNodeIds: new Set(), flashingEdgeIds: new Set() });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('marks a node as flashing and clears it once the flash is over', () => {
    const { result } = renderHook(() => useFlashTimer());

    act(() => result.current.triggerFlashNode('thing-1'));
    expect(flashedNodes()).toEqual(['thing-1']);

    act(() => vi.advanceTimersByTime(FLASH_DURATION_MS));
    expect(flashedNodes()).toEqual([]);
  });

  it('marks an edge as flashing and clears it once the flash is over', () => {
    const { result } = renderHook(() => useFlashTimer());

    act(() => result.current.triggerFlashEdge('edge-1'));
    expect(flashedEdges()).toEqual(['edge-1']);

    act(() => vi.advanceTimersByTime(FLASH_DURATION_MS));
    expect(flashedEdges()).toEqual([]);
  });

  it('keeps the node lit for the full duration after each update rather than the first', () => {
    const { result } = renderHook(() => useFlashTimer());

    act(() => result.current.triggerFlashNode('thing-1'));
    act(() => vi.advanceTimersByTime(FLASH_DURATION_MS - 100));

    act(() => result.current.triggerFlashNode('thing-1'));
    act(() => vi.advanceTimersByTime(FLASH_DURATION_MS - 100));

    // The first flash's timer would have fired by now; restarting it keeps the node lit.
    expect(flashedNodes()).toEqual(['thing-1']);

    act(() => vi.advanceTimersByTime(100));
    expect(flashedNodes()).toEqual([]);
  });

  it('flashes several things at once, each on its own timer', () => {
    const { result } = renderHook(() => useFlashTimer());

    act(() => result.current.triggerFlashNode('first'));
    act(() => vi.advanceTimersByTime(200));
    act(() => result.current.triggerFlashNode('second'));

    expect(flashedNodes()).toEqual(['first', 'second']);

    act(() => vi.advanceTimersByTime(300));
    expect(flashedNodes()).toEqual(['second']);

    act(() => vi.advanceTimersByTime(200));
    expect(flashedNodes()).toEqual([]);
  });

  it('tracks a node and an edge of the same name separately', () => {
    const { result } = renderHook(() => useFlashTimer());

    act(() => {
      result.current.triggerFlashNode('same-id');
      result.current.triggerFlashEdge('same-id');
    });

    expect(flashedNodes()).toEqual(['same-id']);
    expect(flashedEdges()).toEqual(['same-id']);
  });

  // A timer firing after the view has gone would write to a store nothing is showing.
  it('drops pending flashes when the view goes away', () => {
    const { result, unmount } = renderHook(() => useFlashTimer());

    act(() => result.current.triggerFlashNode('thing-1'));
    unmount();

    act(() => vi.advanceTimersByTime(FLASH_DURATION_MS * 2));

    expect(vi.getTimerCount()).toBe(0);
  });
});
