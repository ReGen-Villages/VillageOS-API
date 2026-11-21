import { describe, expect, it } from 'vitest';
import { renderHook } from '@testing-library/react';
import { usePreventsSelectionWhileDragging } from './usePreventsSelectionWhileDragging';

function selectionStarts(): boolean {
  return document.dispatchEvent(new Event('selectstart', { cancelable: true }));
}

describe('usePreventsSelectionWhileDragging', () => {
  it('refuses a selection only while a drag is under way', () => {
    const dragging = { current: false };
    renderHook(() => usePreventsSelectionWhileDragging(dragging));

    expect(selectionStarts()).toBe(true);
    dragging.current = true;
    expect(selectionStarts()).toBe(false);
    dragging.current = false;
    expect(selectionStarts()).toBe(true);
  });

  it('stops listening once the component is gone', () => {
    const dragging = { current: true };
    const { unmount } = renderHook(() => usePreventsSelectionWhileDragging(dragging));

    unmount();

    expect(selectionStarts()).toBe(true);
  });
});
