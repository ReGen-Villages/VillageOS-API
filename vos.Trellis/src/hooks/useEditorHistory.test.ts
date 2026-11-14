import { describe, it, expect, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useEditorHistory } from './useEditorHistory';

describe('useEditorHistory', () => {
  it('does nothing on Undo when nothing has been recorded', () => {
    const setNodes = vi.fn();
    const setEdges = vi.fn();
    const onUndone = vi.fn();
    const { result } = renderHook(() => useEditorHistory({ nodes: [], edges: [], setNodes, setEdges, onUndone }));

    act(() => result.current.onUndo());

    expect(setNodes).not.toHaveBeenCalled();
    expect(onUndone).not.toHaveBeenCalled();
    expect(result.current.canUndo).toBe(false);
  });
});
