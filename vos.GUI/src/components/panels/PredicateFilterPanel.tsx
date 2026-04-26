import { useMemo, useState } from 'react';
import { ChevronDown, ChevronRight, Eye, EyeOff, X } from 'lucide-react';
import { useUiStore } from '../../stores/uiStore';
import type { PredicateStats } from '../../utils/predicateCluster';

/**
 * Feature #5362 — predicate filter panel, sibling to TypeFilterPanel.
 *
 * Same shape and interaction model as the type filter (collapsible, search,
 * checkbox per row, All/None actions) but reads/writes the existing
 * `activePredicateIds` set in uiStore — which the NodeReducer already uses
 * as an inclusive edge-visibility filter and a clustering trigger.
 *
 * Semantics:
 *   - When NO predicates are checked: no filter is applied — every edge
 *     renders. The header shows the total predicate count.
 *   - When SOME predicates are checked: only edges whose predicate is in
 *     the active set render, AND ClusterComputer kicks in around them.
 *   - "All" → activate every discovered predicate (cluster around all).
 *   - "None" → clear the active set (default behavior, all edges visible).
 *
 * Co-located with the TypeFilterPanel so users see one filter region for
 * both Things and Relationships.
 */
export function PredicateFilterPanel() {
  const predicateStats = useUiStore((s) => s.predicateStats);
  const activePredicateIds = useUiStore((s) => s.activePredicateIds);
  const togglePredicateId = useUiStore((s) => s.togglePredicateId);
  const setPredicateIds = useUiStore((s) => s.setPredicateIds);
  const clearPredicateIds = useUiStore((s) => s.clearPredicateIds);

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

  const activeEdges = useMemo(() => {
    let sum = 0;
    for (const s of predicateStats) if (activePredicateIds.has(s.predicateId)) sum += s.edgeCount;
    return sum;
  }, [predicateStats, activePredicateIds]);

  // Header count: when filter is empty, every edge is shown (=totalEdges
  // visible). When non-empty, the active set is what's visible.
  const visibleEdges = activePredicateIds.size === 0 ? totalEdges : activeEdges;

  const activateAll = () => setPredicateIds(new Set(predicateStats.map((s) => s.predicateId)));
  const clearAll = () => clearPredicateIds();

  if (predicateStats.length === 0) return null;

  return (
    <div className="border border-zinc-700/60 rounded-lg bg-zinc-800/60 backdrop-blur text-zinc-200 text-xs overflow-hidden">
      <button
        type="button"
        onClick={() => setCollapsed((c) => !c)}
        className="w-full flex items-center justify-between px-3 py-2 hover:bg-zinc-700/40 transition-colors"
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
        <div className="border-t border-zinc-700/60">
          <div className="px-3 py-2 flex items-center gap-2">
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

          <div className="px-3 pb-2 flex items-center gap-2">
            <button
              type="button"
              onClick={activateAll}
              className="flex items-center gap-1 px-2 py-1 rounded bg-zinc-700/50 hover:bg-zinc-600/50 text-zinc-200"
            >
              <Eye size={12} /> All
            </button>
            <button
              type="button"
              onClick={clearAll}
              className="flex items-center gap-1 px-2 py-1 rounded bg-zinc-700/50 hover:bg-zinc-600/50 text-zinc-200"
            >
              <EyeOff size={12} /> None
            </button>
            {activePredicateIds.size > 0 && (
              <span className="ml-auto text-zinc-400 text-[10px]">
                {activePredicateIds.size} active
              </span>
            )}
          </div>

          <ul className="max-h-[40vh] overflow-y-auto border-t border-zinc-700/60 divide-y divide-zinc-700/40">
            {filtered.length === 0 ? (
              <li className="px-3 py-2 text-zinc-500 italic">No matching predicates.</li>
            ) : (
              filtered.map((s) => {
                const active = activePredicateIds.has(s.predicateId);
                // When NO filter is active, every predicate's edges are
                // visible — show the row in its normal style. When some are
                // active, dim the inactive rows.
                const isVisible = activePredicateIds.size === 0 || active;
                return (
                  <li key={s.predicateId}>
                    <label className="flex items-center justify-between gap-2 px-3 py-1.5 hover:bg-zinc-700/30 cursor-pointer">
                      <div className="flex items-center gap-2 flex-1 min-w-0">
                        <input
                          type="checkbox"
                          checked={active}
                          onChange={() => togglePredicateId(s.predicateId)}
                          className="accent-violet-500"
                        />
                        <span
                          className="inline-block w-2 h-2 rounded-full flex-shrink-0"
                          style={{ backgroundColor: s.color }}
                          aria-hidden="true"
                        />
                        <span className={`truncate ${isVisible ? 'text-zinc-200' : 'text-zinc-500 line-through'}`}>
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
