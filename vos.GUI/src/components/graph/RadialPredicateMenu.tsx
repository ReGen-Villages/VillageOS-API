import { useEffect, useCallback, useRef } from 'react';
import { useUiStore } from '../../stores/uiStore';

/**
 * Radial predicate selector menu — opens on right-click of the graph background.
 *
 * Shows all predicates arranged in a circle around the cursor position.
 * Each slice displays the predicate name, edge count, and color stripe.
 * Supports multi-select: clicking a predicate toggles it on/off with a checkmark.
 * The most-used predicate sits at 12 o'clock, proceeding clockwise by usage.
 * Centre button clears all selections and closes the menu.
 *
 * Rendered as a sibling to SigmaCanvas (outside SigmaContainer), absolutely
 * positioned within the graph page's relative container.
 */
export function RadialPredicateMenu() {
  const open = useUiStore((s) => s.radialMenuOpen);
  const position = useUiStore((s) => s.radialMenuPosition);
  const predicateStats = useUiStore((s) => s.predicateStats);
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);
  const togglePredicateId = useUiStore((s) => s.togglePredicateId);
  const clearPredicateIds = useUiStore((s) => s.clearPredicateIds);
  const closeRadialMenu = useUiStore((s) => s.closeRadialMenu);
  const menuRef = useRef<HTMLDivElement>(null);

  // Close on Escape
  useEffect(() => {
    if (!open) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        closeRadialMenu();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [open, closeRadialMenu]);

  // Close on click outside
  useEffect(() => {
    if (!open) return;
    const handleClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        closeRadialMenu();
      }
    };
    // Use a short delay to avoid the same click that opened the menu
    const timer = setTimeout(() => {
      window.addEventListener('mousedown', handleClick);
    }, 100);
    return () => {
      clearTimeout(timer);
      window.removeEventListener('mousedown', handleClick);
    };
  }, [open, closeRadialMenu]);

  const handleToggle = useCallback(
    (predicateId: string) => {
      togglePredicateId(predicateId);
      // Menu stays open for multi-select
    },
    [togglePredicateId],
  );

  const handleClear = useCallback(() => {
    clearPredicateIds();
    closeRadialMenu();
  }, [clearPredicateIds, closeRadialMenu]);

  if (!open || !position || predicateStats.length === 0) return null;

  const RADIUS = 130; // distance from centre to each button
  const BUTTON_SIZE = 56; // px, button diameter
  const count = predicateStats.length;
  const hasActive = activePredicateIds.size > 0;

  return (
    <div
      ref={menuRef}
      className="absolute z-50 pointer-events-none"
      style={{
        left: position.x,
        top: position.y,
        transform: 'translate(-50%, -50%)',
      }}
    >
      {/* Centre clear button */}
      <button
        onClick={handleClear}
        className="pointer-events-auto absolute rounded-full bg-zinc-800/90 backdrop-blur border border-zinc-700
                   text-zinc-300 text-xs font-medium hover:bg-zinc-700 hover:text-white transition-all
                   shadow-lg shadow-black/40"
        style={{
          width: 44,
          height: 44,
          left: -22,
          top: -22,
        }}
        title="Clear all predicates"
      >
        {hasActive ? '\u2715' : '\u2022'}
      </button>

      {/* Predicate slices arranged radially */}
      {predicateStats.map((stat, i) => {
        // Angle: start at -90deg (12 o'clock), go clockwise
        const angle = ((2 * Math.PI) / count) * i - Math.PI / 2;
        const x = Math.cos(angle) * RADIUS;
        const y = Math.sin(angle) * RADIUS;
        const isActive = activePredicateIds.has(stat.predicateId);

        return (
          <button
            key={stat.predicateId}
            onClick={() => handleToggle(stat.predicateId)}
            className={`pointer-events-auto absolute rounded-lg backdrop-blur border
                        text-xs transition-all shadow-lg shadow-black/30
                        ${
                          isActive
                            ? 'bg-zinc-700/95 border-zinc-500 text-white scale-110'
                            : 'bg-zinc-800/90 border-zinc-700 text-zinc-300 hover:bg-zinc-700 hover:text-white hover:scale-105'
                        }`}
            style={{
              width: BUTTON_SIZE,
              height: BUTTON_SIZE,
              left: x - BUTTON_SIZE / 2,
              top: y - BUTTON_SIZE / 2,
            }}
            title={`${stat.predicateName} (${stat.edgeCount} edges)`}
          >
            {/* Color stripe */}
            <div
              className="absolute top-0 left-0 right-0 h-1 rounded-t-lg"
              style={{ backgroundColor: stat.color }}
            />
            {/* Checkmark for active predicates */}
            {isActive && (
              <div className="absolute top-0.5 right-0.5 w-3.5 h-3.5 rounded-full bg-blue-500 flex items-center justify-center">
                <span className="text-[8px] text-white font-bold">{'\u2713'}</span>
              </div>
            )}
            {/* Predicate name */}
            <div className="mt-1 font-medium truncate px-1 text-[10px] leading-tight">
              {stat.predicateName}
            </div>
            {/* Edge count */}
            <div className="text-[9px] text-zinc-500 mt-0.5">
              {stat.edgeCount}
            </div>
          </button>
        );
      })}
    </div>
  );
}
