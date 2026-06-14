import { useMemo, useState } from 'react';
import { ChevronDown, ChevronRight, Eye, EyeOff, X } from 'lucide-react';
import { useUiStore } from '../../stores/uiStore';
import type { PredicateStats } from '../../utils/predicateCluster';

/**
 * Feature #5362 — predicate filter panel, sibling to TypeFilterPanel.
 *
 * Bug #5365 — checkbox semantics now mirror the type filter exactly:
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
  const predicateStats = useUiStore((s) => s.predicateStats);
  const hiddenPredicateIds = useUiStore((s) => s.hiddenPredicateIds);
  const toggleHiddenPredicate = useUiStore((s) => s.toggleHiddenPredicate);
  const setHiddenPredicateIds = useUiStore((s) => s.setHiddenPredicateIds);
  const clearHiddenPredicateIds = useUiStore((s) => s.clearHiddenPredicateIds);

  const [collapsed, setCollapsed] = useState(false);
  const [search, setSearch] = useState('');

  const sorted = useMemo<PredicateStats[]>(
    () => [...predicateStats].sort(
      (a, b) => b.edgeCount - a.edgeCount || a.predicateName.localeCompare(b.predicateName),
    ),
    [predicateStats],
  );

  const filtered = useMemo(() => {
    if (!search) return sorted;
    const q = search.toLowerCase();
    return sorted.filter((s) => s.predicateName.toLowerCase().includes(q));
  }, [sorted, search]);

  const totalEdges = useMemo(
    () => predicateStats.reduce((sum, s) => sum + s.edgeCount, 0),
    [predicateStats],
  );

  const hiddenEdges = useMemo(() => {
    let sum = 0;
    for (const s of predicateStats) if (hiddenPredicateIds.has(s.predicateId)) sum += s.edgeCount;
    return sum;
  }, [predicateStats, hiddenPredicateIds]);

  const visibleEdges = totalEdges - hiddenEdges;

  const showAll = () => clearHiddenPredicateIds();
  const hideAll = () => setHiddenPredicateIds(new Set(predicateStats.map((s) => s.predicateId)));

  if (predicateStats.length === 0) return null;

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
          <span>Filter by Predicate</span>
        </div>
        <span className="text-zinc-400 font-mono">
          {visibleEdges.toLocaleString()}/{totalEdges.toLocaleString()}
        </span>
      </button>

      {!collapsed && (
        <div className="border-t border-zinc-700/60 flex flex-col min-h-0 flex-1">
          <div className="px-3 py-2 flex items-center gap-2 flex-shrink-0">
            <input
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search predicates…"
              className="flex-1 bg-zinc-900/60 border border-zinc-700 rounded px-2 py-1 text-xs text-zinc-200 placeholder-zinc-500 focus:outline-none focus:border-zinc-500"
            />
            {search && (
              <button
                type="button"
                onClick={() => setSearch('')}
                className="text-zinc-400 hover:text-zinc-200"
                aria-label="Clear search"
              >
                <X size={14} />
              </button>
            )}
          </div>

          <div className="px-3 pb-2 flex items-center gap-2 flex-shrink-0">
            <button
              type="button"
              onClick={showAll}
              className="flex items-center gap-1 px-2 py-1 rounded bg-zinc-700/50 hover:bg-zinc-600/50 text-zinc-200"
            >
              <Eye size={12} /> All
            </button>
            <button
              type="button"
              onClick={hideAll}
              className="flex items-center gap-1 px-2 py-1 rounded bg-zinc-700/50 hover:bg-zinc-600/50 text-zinc-200"
            >
              <EyeOff size={12} /> None
            </button>
            {hiddenEdges > 0 && (
              <span className="ml-auto text-zinc-400 text-[10px]">
                {hiddenEdges.toLocaleString()} hidden
              </span>
            )}
          </div>

          <ul className="flex-1 min-h-0 overflow-y-auto border-t border-zinc-700/60 divide-y divide-zinc-700/40">
            {filtered.length === 0 ? (
              <li className="px-3 py-2 text-zinc-500 italic">No matching predicates.</li>
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
                      <span className="text-[10px] font-mono text-zinc-400">{s.edgeCount.toLocaleString()}</span>
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
