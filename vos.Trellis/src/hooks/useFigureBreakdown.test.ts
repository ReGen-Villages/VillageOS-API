import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import type { Binding } from '../types/dashboard';
import { buildModelIndex, type ResolveContext } from '../api/dashboardApi';
import type { FigureBreakdown } from '../api/figureBreakdown';

const mockBreakdownOf = vi.fn();
vi.mock('../api/figureBreakdown', () => ({
  breakdownOf: (binding: Binding, ctx: ResolveContext) => mockBreakdownOf(binding, ctx),
}));

const { useFigureBreakdown } = await import('./useFigureBreakdown');

const COUNT: Binding = { kind: 'stateCount', state: 'flooded' };
const OTHER: Binding = { kind: 'stateCount', state: 'dry' };
const context = (): ResolveContext => ({ idx: buildModelIndex([], []), scopeId: null }) as ResolveContext;

function answer(value: number): FigureBreakdown {
  return { value, terms: {}, behind: null };
}

describe('useFigureBreakdown', () => {
  beforeEach(() => {
    mockBreakdownOf.mockReset();
  });

  it('reads and hands over what the figure is made of', async () => {
    mockBreakdownOf.mockResolvedValue(answer(4));
    const { result } = renderHook(() => useFigureBreakdown(COUNT, context()));

    expect(result.current.loading).toBe(true);
    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.breakdown?.value).toBe(4);
  });

  it('reads again when the context moves on, keeping the answer on screen until the next lands', async () => {
    mockBreakdownOf.mockResolvedValue(answer(4));
    const ctx = context();
    const { result, rerender } = renderHook(({ ctx }) => useFigureBreakdown(COUNT, ctx), { initialProps: { ctx } });
    await waitFor(() => expect(result.current.breakdown?.value).toBe(4));

    let release!: (value: FigureBreakdown) => void;
    mockBreakdownOf.mockImplementation(() => new Promise((resolve) => { release = resolve; }));
    rerender({ ctx: context() });

    expect(mockBreakdownOf).toHaveBeenCalledTimes(2);
    expect(result.current.breakdown?.value).toBe(4);
    release(answer(5));
    await waitFor(() => expect(result.current.breakdown?.value).toBe(5));
  });

  it('answers the binding it is now asked about, never one it was asked about before', async () => {
    let releaseFirst!: (value: FigureBreakdown) => void;
    mockBreakdownOf
      .mockImplementationOnce(() => new Promise((resolve) => { releaseFirst = resolve; }))
      .mockResolvedValueOnce(answer(9));
    const ctx = context();
    const { result, rerender } = renderHook(({ binding }) => useFigureBreakdown(binding, ctx), { initialProps: { binding: COUNT } });

    rerender({ binding: OTHER });
    await waitFor(() => expect(result.current.breakdown?.value).toBe(9));
    releaseFirst(answer(1));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(result.current.breakdown?.value).toBe(9);
  });

  it('shows nothing rather than a stale figure when the read is refused', async () => {
    mockBreakdownOf.mockRejectedValue(new Error('refused'));
    const { result } = renderHook(() => useFigureBreakdown(COUNT, context()));

    await waitFor(() => expect(result.current.loading).toBe(false));
    expect(result.current.breakdown).toBeNull();
  });
});
