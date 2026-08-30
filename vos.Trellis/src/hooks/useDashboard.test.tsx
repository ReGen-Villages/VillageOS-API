import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useResolveContext } from './useDashboard';
import { buildModelIndex } from '../api/dashboardApi';
import type { ModelReads } from '../api/modelReads';

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
