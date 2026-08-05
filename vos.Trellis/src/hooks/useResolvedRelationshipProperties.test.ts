import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { useResolvedRelationshipProperties } from './useResolvedRelationshipProperties';

const mockGetEffectiveProperties = vi.fn();

vi.mock('../api/relationshipApi', () => ({
  relationshipApi: {
    getEffectiveProperties: (...args: unknown[]) => mockGetEffectiveProperties(...args),
  },
}));

const quantity = { quantity: { Value: 3, Type: 'vos.Decimal', IsInherited: false } };

beforeEach(() => {
  vi.clearAllMocks();
});

describe('useResolvedRelationshipProperties', () => {
  it('reads the relationship it was given', async () => {
    mockGetEffectiveProperties.mockResolvedValue(quantity);
    const { result } = renderHook(() => useResolvedRelationshipProperties('rel-1'));
    await waitFor(() => expect(result.current).toEqual(quantity));
    expect(mockGetEffectiveProperties).toHaveBeenCalledWith('rel-1');
  });

  it('holds nothing until the answer arrives', () => {
    mockGetEffectiveProperties.mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useResolvedRelationshipProperties('rel-1'));
    expect(result.current).toBeNull();
  });

  // A failed read must not leave a stale set standing: a caller uses it to name a property's type,
  // and naming the wrong one is worse than offering no editing.
  it('holds nothing when the read fails', async () => {
    mockGetEffectiveProperties.mockRejectedValue(new Error('unreachable'));
    const { result } = renderHook(() => useResolvedRelationshipProperties('rel-1'));
    await waitFor(() => expect(mockGetEffectiveProperties).toHaveBeenCalled());
    expect(result.current).toBeNull();
  });

  it('reads again when the version moves, because a write can change what is resolved', async () => {
    mockGetEffectiveProperties.mockResolvedValue(quantity);
    const { rerender } = renderHook(({ version }) => useResolvedRelationshipProperties('rel-1', { version }), {
      initialProps: { version: 0 },
    });
    await waitFor(() => expect(mockGetEffectiveProperties).toHaveBeenCalledTimes(1));
    rerender({ version: 1 });
    await waitFor(() => expect(mockGetEffectiveProperties).toHaveBeenCalledTimes(2));
  });

  it('does not read again when nothing it depends on moved', async () => {
    mockGetEffectiveProperties.mockResolvedValue(quantity);
    const { rerender } = renderHook(() => useResolvedRelationshipProperties('rel-1'));
    await waitFor(() => expect(mockGetEffectiveProperties).toHaveBeenCalledTimes(1));
    rerender();
    expect(mockGetEffectiveProperties).toHaveBeenCalledTimes(1);
  });

  // The edge panel opens on its Ranges tab. Reading properties nobody has looked at would cost a
  // request per edge a user clicks through.
  it('asks nothing while disabled, and reads once enabled', async () => {
    mockGetEffectiveProperties.mockResolvedValue(quantity);
    const { rerender } = renderHook(({ enabled }) => useResolvedRelationshipProperties('rel-1', { enabled }), {
      initialProps: { enabled: false },
    });
    expect(mockGetEffectiveProperties).not.toHaveBeenCalled();
    rerender({ enabled: true });
    await waitFor(() => expect(mockGetEffectiveProperties).toHaveBeenCalledTimes(1));
  });
});
