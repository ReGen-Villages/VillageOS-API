import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { numberIn } from '../../i18n/numbers';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { FilterControls } from './FilterControls';
import { useUiStore } from '../../stores/uiStore';
import type { PredicateStatistics } from '../../utils/predicateCluster';
import { TABLET_WIDTH, useIsWide } from '../../hooks/useIsWide';

/**
 * Predicate filter panel, sibling to TypeFilterPanel.
 *
 * Checkbox semantics mirror the type filter exactly:
 *   - Checked = predicate's edges are visible.
 *   - Unchecked = predicate's edges are hidden.
 *   - Default state on a fresh model is all-checked (everything visible).
 *   - All button = clear hidden set (all edges visible).
 *   - None button = add every predicate to the hidden set (no edges visible).
 *
 * Driven by `hiddenPredicateIds` in uiStore. Independent from
 * `activePredicateIds` which still drives clustering via the radial menu —
 * different intent. NodeReducer.edgeReducer hides any edge whose
 * predicateId is in `hiddenPredicateIds`.
 */
export function PredicateFilterPanel() {
  const { t } = useTranslation();
  const predicateStatistics = useUiStore((s) => s.predicateStatistics);
  const hiddenPredicateIds = useUiStore((s) => s.hiddenPredicateIds);
  const toggleHiddenPredicate = useUiStore((s) => s.toggleHiddenPredicate);
  const setHiddenPredicateIds = useUiStore((s) => s.setHiddenPredicateIds);
  const clearHiddenPredicateIds = useUiStore((s) => s.clearHiddenPredicateIds);

  const wide = useIsWide(TABLET_WIDTH);
  const [collapsed, setCollapsed] = useState(!wide);
  const [search, setSearch] = useState('');

  const sorted = useMemo<PredicateStatistics[]>(
    () => [...predicateStatistics].sort(
      (a, b) => b.edgeCount - a.edgeCount || a.predicateName.localeCompare(b.predicateName),
    ),
    [predicateStatistics],
  );

  const filtered = useMemo(() => {
    if (!search) return sorted;
    const q = search.toLowerCase();
    return sorted.filter((s) => s.predicateName.toLowerCase().includes(q));
  }, [sorted, search]);

  const totalEdges = useMemo(
    () => predicateStatistics.reduce((sum, s) => sum + s.edgeCount, 0),
    [predicateStatistics],
  );

  const hiddenEdges = useMemo(() => {
    let sum = 0;
    for (const s of predicateStatistics) if (hiddenPredicateIds.has(s.predicateId)) sum += s.edgeCount;
    return sum;
  }, [predicateStatistics, hiddenPredicateIds]);

  const visibleEdges = totalEdges - hiddenEdges;

  const showAll = () => clearHiddenPredicateIds();
  const hideAll = () => setHiddenPredicateIds(new Set(predicateStatistics.map((s) => s.predicateId)));

  if (predicateStatistics.length === 0) return null;

  return (
    <div className={`border border-zinc-700/60 rounded-lg bg-zinc-800/60 backdrop-blur text-zinc-200 text-xs overflow-hidden flex flex-col ${collapsed ? 'flex-shrink-0' : 'flex-1 min-h-0'}`}>
      <button
        type="button"
        onClick={() => setCollapsed((c) => !c)}
        className="w-full flex items-center justify-between px-3 py-2 hover:bg-zinc-700/40 transition-colors flex-shrink-0"
        aria-expanded={!collapsed}
      >
        <div className="flex items-center gap-2 font-semibold">
          {collapsed ? <ChevronRight size={14} /> : <ChevronDown size={14} />}
          <span>{t('graph.predicateFilter.title')}</span>
        </div>
        <span className="text-zinc-400 font-mono">
          {numberIn(visibleEdges)}/{numberIn(totalEdges)}
        </span>
      </button>

      {!collapsed && (
        <div className="border-t border-zinc-700/60 flex flex-col min-h-0 flex-1">
          <FilterControls
            search={search}
            onSearch={setSearch}
            placeholder={t('graph.predicateFilter.search')}
            onShowAll={showAll}
            onHideAll={hideAll}
            hidden={hiddenEdges}
          />

          <ul className="flex-1 min-h-0 overflow-y-auto border-t border-zinc-700/60 divide-y divide-zinc-700/40">
            {filtered.length === 0 ? (
              <li className="px-3 py-2 text-zinc-500 italic">{t('graph.predicateFilter.noMatch')}</li>
            ) : (
              filtered.map((s) => {
                const visible = !hiddenPredicateIds.has(s.predicateId);
                return (
                  <li key={s.predicateId}>
                    <label className="flex items-center justify-between gap-2 px-3 py-1.5 hover:bg-zinc-700/30 cursor-pointer">
                      <div className="flex items-center gap-2 flex-1 min-w-0">
                        <input
                          type="checkbox"
                          checked={visible}
                          onChange={() => toggleHiddenPredicate(s.predicateId)}
                          className="accent-violet-500"
                        />
                        <span
                          className="inline-block w-2 h-2 rounded-full flex-shrink-0"
                          style={{ backgroundColor: s.color }}
                          aria-hidden="true"
                        />
                        <span className="truncate text-zinc-200">
                          {s.predicateName}
                        </span>
                      </div>
                      <span className="text-[10px] font-mono text-zinc-400">{numberIn(s.edgeCount)}</span>
                    </label>
                  </li>
                );
              })
            )}
          </ul>
        </div>
      )}
    </div>
  );
}
