import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { useDeclaredPropertyTypes } from './useDeclaredPropertyTypes';
import { useModelStore } from '../stores/modelStore';

const mockGetAllProperties = vi.fn();

vi.mock('../api/thingApi', () => ({
  thingApi: { getAllProperties: (...args: unknown[]) => mockGetAllProperties(...args) },
}));

const doorNumber = { 'thing-1': { door_number: { Value: '4711', Type: 'vos.String', IsInherited: false } } };
const openRatio = { 'thing-9': { open_ratio: { Value: 0.5, Type: 'vos.Double', IsInherited: false } } };

beforeEach(() => {
  vi.clearAllMocks();
  useModelStore.setState({ things: [], relationships: [], loaded: true });
});

describe('useDeclaredPropertyTypes', () => {
  it('reads the resolved set once the model is loaded', async () => {
    mockGetAllProperties.mockResolvedValue(doorNumber);
    const { result } = renderHook(() => useDeclaredPropertyTypes());
    await waitFor(() => expect(result.current).toEqual(doorNumber));
    expect(mockGetAllProperties).toHaveBeenCalledWith('effective', undefined);
  });

  it('holds nothing until the answer arrives, so a caller formats by shape meanwhile', () => {
    mockGetAllProperties.mockReturnValue(new Promise(() => {}));
    const { result } = renderHook(() => useDeclaredPropertyTypes());
    expect(result.current).toBeNull();
  });

  it('reads nothing for a surface that has not asked', () => {
    renderHook(() => useDeclaredPropertyTypes(false));
    expect(mockGetAllProperties).not.toHaveBeenCalled();
  });

  it('reads once however often it re-renders', async () => {
    mockGetAllProperties.mockResolvedValue(doorNumber);
    const { result, rerender } = renderHook(() => useDeclaredPropertyTypes());
    await waitFor(() => expect(result.current).toEqual(doorNumber));
    rerender();
    rerender();
    expect(mockGetAllProperties).toHaveBeenCalledTimes(1);
  });

  // Switching model clears the store rather than remounting the app. A read that never repeated
  // would keep describing the model that was open before, and those ids belong to that model.
  it('reads again for the next model, and shows nothing from the last one meanwhile', async () => {
    mockGetAllProperties.mockResolvedValue(doorNumber);
    const { result } = renderHook(() => useDeclaredPropertyTypes());
    await waitFor(() => expect(result.current).toEqual(doorNumber));

    mockGetAllProperties.mockResolvedValue(openRatio);
    act(() => useModelStore.getState().clear());
    expect(result.current).toBeNull();

    act(() => useModelStore.getState().markLoaded());
    await waitFor(() => expect(result.current).toEqual(openRatio));
    expect(mockGetAllProperties).toHaveBeenCalledTimes(2);
  });

  // A failed read settles on an empty set rather than staying null for ever, which would leave a
  // caller waiting on a read that is never coming back.
  it('settles empty when the read fails', async () => {
    mockGetAllProperties.mockRejectedValue(new Error('no'));
    const { result } = renderHook(() => useDeclaredPropertyTypes());
    await waitFor(() => expect(result.current).toEqual({}));
  });

  // #6189 — reading every Thing's resolved properties is more than the model load itself on a large
  // model. A surface showing a handful of rows should read a handful.
  describe('narrowing to the things a surface shows', () => {
    it('reads only the ids it was given', async () => {
      mockGetAllProperties.mockResolvedValue(doorNumber);
      const { result } = renderHook(() => useDeclaredPropertyTypes(true, ['thing-1']));
      await waitFor(() => expect(result.current).toEqual(doorNumber));
      expect(mockGetAllProperties).toHaveBeenCalledWith('effective', ['thing-1']);
    });

    it('does not read an id it already holds', async () => {
      mockGetAllProperties.mockResolvedValue(doorNumber);
      const { result, rerender } = renderHook(
        ({ ids }: { ids: string[] }) => useDeclaredPropertyTypes(true, ids),
        { initialProps: { ids: ['thing-1'] } },
      );
      await waitFor(() => expect(result.current).toEqual(doorNumber));

      rerender({ ids: ['thing-1'] });
      expect(mockGetAllProperties).toHaveBeenCalledTimes(1);
    });

    it('reads only what is new when the set grows, and keeps what it had', async () => {
      mockGetAllProperties.mockResolvedValue(doorNumber);
      const { result, rerender } = renderHook(
        ({ ids }: { ids: string[] }) => useDeclaredPropertyTypes(true, ids),
        { initialProps: { ids: ['thing-1'] } },
      );
      await waitFor(() => expect(result.current).toEqual(doorNumber));

      mockGetAllProperties.mockResolvedValue(openRatio);
      rerender({ ids: ['thing-1', 'thing-9'] });

      await waitFor(() => expect(result.current).toEqual({ ...doorNumber, ...openRatio }));
      expect(mockGetAllProperties).toHaveBeenLastCalledWith('effective', ['thing-9']);
    });

    it('reads nothing when the surface is showing nothing', () => {
      renderHook(() => useDeclaredPropertyTypes(true, []));
      expect(mockGetAllProperties).not.toHaveBeenCalled();
    });

    // The order rows happen to be in is not a different question.
    it('does not read again just because the ids arrived in another order', async () => {
      mockGetAllProperties.mockResolvedValue({ ...doorNumber, ...openRatio });
      const { result, rerender } = renderHook(
        ({ ids }: { ids: string[] }) => useDeclaredPropertyTypes(true, ids),
        { initialProps: { ids: ['thing-1', 'thing-9'] } },
      );
      await waitFor(() => expect(result.current).not.toBeNull());

      rerender({ ids: ['thing-9', 'thing-1'] });
      expect(mockGetAllProperties).toHaveBeenCalledTimes(1);
    });
  });
});
