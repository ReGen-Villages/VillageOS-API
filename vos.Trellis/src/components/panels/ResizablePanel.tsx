import { useRef, useCallback, useEffect, type ReactNode } from 'react';
import { useUiStore, MIN_PANEL_WIDTH, MAX_PANEL_WIDTH } from '../../stores/uiStore';

interface Props {
  children: ReactNode;
}

export function ResizablePanel({ children }: Props) {
  const width = useUiStore((s) => s.detailPanelWidth);
  const setWidth = useUiStore((s) => s.setDetailPanelWidth);
  const dragging = useRef(false);
  const startX = useRef(0);
  const startWidth = useRef(0);
  const containerRef = useRef<HTMLDivElement>(null);

  const getMaxWidth = useCallback(() => {
    const parent = containerRef.current?.parentElement;
    if (!parent) return MAX_PANEL_WIDTH;
    return Math.min(MAX_PANEL_WIDTH, Math.floor(parent.clientWidth * 0.9));
  }, []);

  const onPointerDown = useCallback(
    (e: React.PointerEvent) => {
      e.preventDefault();
      dragging.current = true;
      startX.current = e.clientX;
      startWidth.current = width;
      (e.target as HTMLElement).setPointerCapture(e.pointerId);
    },
    [width],
  );

  const onPointerMove = useCallback(
    (e: React.PointerEvent) => {
      if (!dragging.current) return;
      const delta = startX.current - e.clientX;
      const maxW = getMaxWidth();
      const newWidth = Math.max(MIN_PANEL_WIDTH, Math.min(maxW, startWidth.current + delta));
      setWidth(newWidth);
    },
    [setWidth, getMaxWidth],
  );

  const onPointerUp = useCallback(() => {
    dragging.current = false;
  }, []);

  // Prevent text selection while dragging
  useEffect(() => {
    const prevent = (e: Event) => {
      if (dragging.current) e.preventDefault();
    };
    document.addEventListener('selectstart', prevent);
    return () => document.removeEventListener('selectstart', prevent);
  }, []);

  return (
    <div
      ref={containerRef}
      className="absolute top-0 right-0 h-full flex z-20"
      style={{ width }}
    >
      {/* Drag handle */}
      <div
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        className="w-1.5 cursor-col-resize flex-shrink-0 group relative"
      >
        <div className="absolute inset-y-0 -left-1 -right-1" />
        <div className="h-full w-px mx-auto bg-zinc-700 group-hover:bg-blue-500 group-active:bg-blue-400 transition-colors" />
      </div>

      {/* Panel content fills remaining space */}
      <div className="flex-1 min-w-0 flex flex-col overflow-hidden bg-zinc-50 dark:bg-zinc-900 border-l border-zinc-200 dark:border-zinc-700 shadow-xl">
        {children}
      </div>
    </div>
  );
}
