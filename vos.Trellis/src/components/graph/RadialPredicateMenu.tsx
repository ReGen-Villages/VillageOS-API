import { useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useClosesOnEscapeOrPressOutside } from '../../hooks/useClosesOnEscapeOrPressOutside';
import { useUiStore } from '../../stores/uiStore';

/** Rendered beside the Sigma canvas, outside its container, and positioned within the graph page. */
export function RadialPredicateMenu() {
  const { t } = useTranslation();
  const open = useUiStore((s) => s.radialMenuOpen);
  const position = useUiStore((s) => s.radialMenuPosition);
  const predicateStatistics = useUiStore((s) => s.predicateStatistics);
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);
  const togglePredicateId = useUiStore((s) => s.togglePredicateId);
  const clearPredicateIds = useUiStore((s) => s.clearPredicateIds);
  const closeRadialMenu = useUiStore((s) => s.closeRadialMenu);
  const menuReference = useRef<HTMLDivElement>(null);
  useClosesOnEscapeOrPressOutside(open, menuReference, closeRadialMenu);

  const handleToggle = useCallback(
    (predicateId: string) => {
      togglePredicateId(predicateId);
    },
    [togglePredicateId],
  );

  const handleClear = useCallback(() => {
    clearPredicateIds();
    closeRadialMenu();
  }, [clearPredicateIds, closeRadialMenu]);

  if (!open || !position || predicateStatistics.length === 0) return null;

  const RADIUS = 130;
  const BUTTON_SIZE = 56;
  const count = predicateStatistics.length;
  const hasActive = activePredicateIds.size > 0;

  return (
    <div
      ref={menuReference}
      className="absolute z-50 pointer-events-none"
      style={{
        left: position.x,
        top: position.y,
        transform: 'translate(-50%, -50%)',
      }}
    >
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
        title={t('graph.radial.clearAll')}
      >
        {hasActive ? '\u2715' : '\u2022'}
      </button>

      {predicateStatistics.map((stat, i) => {
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
            title={t('graph.radial.slice', { name: stat.predicateName, edges: stat.edgeCount })}
          >
            <div
              className="absolute top-0 left-0 right-0 h-1 rounded-t-lg"
              style={{ backgroundColor: stat.color }}
            />
            {isActive && (
              <div className="absolute top-0.5 right-0.5 w-3.5 h-3.5 rounded-full bg-blue-500 flex items-center justify-center">
                <span className="text-[8px] text-white font-bold">{'\u2713'}</span>
              </div>
            )}
            <div className="mt-1 font-medium truncate px-1 text-[10px] leading-tight">
              {stat.predicateName}
            </div>
            <div className="text-[9px] text-zinc-500 mt-0.5">
              {stat.edgeCount}
            </div>
          </button>
        );
      })}
    </div>
  );
}
