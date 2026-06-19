import { useCallback, useEffect, useRef } from 'react';
import { useUiStore } from '../stores/uiStore';

const FLASH_DURATION_MS = 500;

export function useFlashTimer() {
  const addFlashNode = useUiStore((s) => s.addFlashNode);
  const removeFlashNode = useUiStore((s) => s.removeFlashNode);
  const addFlashEdge = useUiStore((s) => s.addFlashEdge);
  const removeFlashEdge = useUiStore((s) => s.removeFlashEdge);

  const timersRef = useRef(new Map<string, ReturnType<typeof setTimeout>>());

  const triggerFlashNode = useCallback((thingId: string) => {
    const timers = timersRef.current;
    const key = `n:${thingId}`;
    const prev = timers.get(key);
    if (prev) clearTimeout(prev);
    addFlashNode(thingId);
    timers.set(key, setTimeout(() => { removeFlashNode(thingId); timers.delete(key); }, FLASH_DURATION_MS));
  }, [addFlashNode, removeFlashNode]);

  const triggerFlashEdge = useCallback((edgeId: string) => {
    const timers = timersRef.current;
    const key = `e:${edgeId}`;
    const prev = timers.get(key);
    if (prev) clearTimeout(prev);
    addFlashEdge(edgeId);
    timers.set(key, setTimeout(() => { removeFlashEdge(edgeId); timers.delete(key); }, FLASH_DURATION_MS));
  }, [addFlashEdge, removeFlashEdge]);

  useEffect(() => {
    const timers = timersRef.current;
    return () => { timers.forEach((t) => clearTimeout(t)); timers.clear(); };
  }, []);

  return { triggerFlashNode, triggerFlashEdge };
}
