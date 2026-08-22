import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useResolveContext } from './useDashboard';
import { buildModelIndex } from '../api/dashboardApi';

// Widgets each resolve their own bindings, so without something shared between them a page
// showing the same state in a count, a funnel stage and a table asks the broker for it three
// times per refresh.
describe('useResolveContext', () => {
  const idx = buildModelIndex([], []);

  it('gives every widget of one refresh generation the same state reads', () => {
    const { result, rerender } = renderHook(({ nonce }) => useResolveContext(idx, null, 'Site', nonce), {
      initialProps: { nonce: 1 },
    });
    const first = result.current.stateMembers;
    rerender({ nonce: 1 });
    expect(result.current.stateMembers).toBe(first);
  });

  // Several verdict rows on one page ask about one study, whose judge-ranges sit on its archetype.
  it('gives every widget of one refresh generation the same range reads', () => {
    const { result, rerender } = renderHook(({ nonce }) => useResolveContext(idx, null, 'Site', nonce), {
      initialProps: { nonce: 1 },
    });
    const first = result.current.thingRanges;
    expect(first).toBeDefined();
    rerender({ nonce: 1 });
    expect(result.current.thingRanges).toBe(first);
    rerender({ nonce: 2 });
    expect(result.current.thingRanges).not.toBe(first);
  });

  it('starts a fresh set of reads when the generation moves on', () => {
    const { result, rerender } = renderHook(({ nonce }) => useResolveContext(idx, null, 'Site', nonce), {
      initialProps: { nonce: 1 },
    });
    const first = result.current.stateMembers;
    rerender({ nonce: 2 });
    expect(result.current.stateMembers).not.toBe(first);
  });
});
