import { useCallback, useEffect, useRef, useState, type Dispatch, type SetStateAction } from 'react';
import type { Edge, Node } from '@xyflow/react';
import { EditorHistory } from '../pipeline/history';

export type EditorSnapshot = { nodes: Node[]; edges: Edge[] };

interface EditorHistoryInputs {
  nodes: Node[];
  edges: Edge[];
  setNodes: Dispatch<SetStateAction<Node[]>>;
  setEdges: Dispatch<SetStateAction<Edge[]>>;
  onUndone: () => void;
}

/**
 * Undo + optimistic rollback. The history records the editor state *before* each mutation (undo),
 * and holds the last server-confirmed state as a baseline (rollback on a rejected save). `canUndo`/`dirty`
 * drive the toolbar. A ref mirrors the latest nodes/edges so any handler can snapshot the current state.
 */
export function useEditorHistory({ nodes, edges, setNodes, setEdges, onUndone }: EditorHistoryInputs) {
  const historyReference = useRef(new EditorHistory<EditorSnapshot>({ nodes: [], edges: [] }));
  const stateReference = useRef<EditorSnapshot>({ nodes, edges });
  // Mirrored after each commit rather than during render, so a handler that snapshots for undo
  // reads what is on screen — never values from a render React went on to discard.
  useEffect(() => { stateReference.current = { nodes, edges }; }, [nodes, edges]);
  const [canUndo, setCanUndo] = useState(false);
  const [dirty, setDirty] = useState(false);

  const recordSnapshot = useCallback(() => {
    historyReference.current.record(structuredClone(stateReference.current));
    setCanUndo(true);
    setDirty(true);
  }, []);

  const applySnapshot = useCallback((s: EditorSnapshot) => {
    setNodes(structuredClone(s.nodes));
    setEdges(structuredClone(s.edges));
  }, [setNodes, setEdges]);

  // Baseline = the last server-confirmed state; also clears the undo stack (a fresh save/load/new is the floor).
  const commitBaseline = useCallback((s: EditorSnapshot) => {
    historyReference.current.commit(structuredClone(s));
    setCanUndo(false);
    setDirty(false);
  }, []);

  const onUndo = useCallback(() => {
    const previous = historyReference.current.undo();
    if (!previous) return;
    applySnapshot(previous);
    setCanUndo(historyReference.current.canUndo());
    setDirty(true);
    onUndone();
  }, [applySnapshot, onUndone]);

  /** Puts the last server-confirmed state back after a rejected save; false when there is none. */
  const rollBack = useCallback(() => {
    const target = historyReference.current.rollbackTarget();
    if (!target) return false;
    applySnapshot(target);
    setCanUndo(false);
    setDirty(false);
    return true;
  }, [applySnapshot]);

  const current = useCallback(() => stateReference.current, []);

  // Ctrl/Cmd+Z undoes the last editor change. Ignored while typing in a field so it doesn't hijack text undo.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && !e.shiftKey && e.key.toLowerCase() === 'z') {
        const el = e.target as HTMLElement | null;
        const tag = el?.tagName;
        if (tag === 'INPUT' || tag === 'TEXTAREA' || el?.isContentEditable) return;
        e.preventDefault();
        onUndo();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onUndo]);

  return { canUndo, dirty, recordSnapshot, commitBaseline, onUndo, rollBack, current };
}
