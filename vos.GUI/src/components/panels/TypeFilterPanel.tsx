import { useMemo, useState } from 'react';
import { ChevronDown, ChevronRight, Eye, EyeOff, X } from 'lucide-react';
import { useModelStore } from '../../stores/modelStore';
import { useUiStore } from '../../stores/uiStore';
import {
  discoverTypes,
  groupTypesByName,
  sortTypeGroups,
  type SortOrder,
  type TypeGroupStat,
} from '../../utils/typeFilter';

/**
 * Feature #5362 — shared type-filter panel rendered inside both the Graph
 * page and the Model page sidebars. Lists every type Thing in the loaded
 * model (any Thing that's the target of an `is` relationship from another
 * Thing) with an instance count and a checkbox. Toggling immediately drops
 * those instances from BOTH visualizations because both consume
 * `useUiStore.hiddenTypeIds`.
 *
 * Bug #5363 — rows are grouped by Name. Multiple type-Things may legitimately
 * share a Name (Name is a display label, not a unique key); the user thinks
 * of them as one category. The row's checkbox controls every underlying
 * typeId in the group together; mixed underlying state renders as
 * indeterminate and normalizes to "all hidden" or "all visible" on click.
 *
 * Domain-agnostic — no string-literal references to IFC class names.
 */
export function TypeFilterPanel() {
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const hiddenTypeIds = useUiStore((s) => s.hiddenTypeIds);
  const setHiddenTypeIds = useUiStore((s) => s.setHiddenTypeIds);
  const clearHiddenTypeIds = useUiStore((s) => s.clearHiddenTypeIds);
  const sortOrder = useUiStore((s) => s.typeSortOrder);
  const setSortOrder = useUiStore((s) => s.setTypeSortOrder);

  const [collapsed, setCollapsed] = useState(false);
  const [search, setSearch] = useState('');

  const allGroups = useMemo<TypeGroupStat[]>(
    () => sortTypeGroups(groupTypesByName(discoverTypes(things, relationships)), sortOrder),
    [things, relationships, sortOrder],
  );

  const filtered = useMemo(() => {
    if (!search) return allGroups;
    const q = search.toLowerCase();
    return allGroups.filter((g) => g.name.toLowerCase().includes(q));
  }, [allGroups, search]);

  const totalInstances = useMemo(
    () => allGroups.reduce((sum, g) => sum + g.instanceCount, 0),
    [allGroups],
  );

  // Per-group view of hidden state — counts how many of the group's typeIds
  // are in hiddenTypeIds. Drives the indeterminate checkbox visual and the
  // toggle action.
  const hiddenCountByGroup = useMemo(() => {
    const m = new Map<string, number>();
    for (const g of allGroups) {
      let n = 0;
      for (const id of g.typeIds) if (hiddenTypeIds.has(id)) n++;
      m.set(g.name, n);
    }
    return m;
  }, [allGroups, hiddenTypeIds]);

  const hiddenInstances = useMemo(() => {
    let sum = 0;
    for (const g of allGroups) {
      // A typeId in the group contributes its share of the group's instance
      // count when hidden. We don't know the per-typeId count here (only the
      // group sum), so approximate by sharing equally — accurate when all
      // typeIds in a group have similar counts; for the panel's header
      // counter this is good enough. Exact accounting would require keeping
      // the original TypeStat[] alongside the groups.
      const hidden = hiddenCountByGroup.get(g.name) ?? 0;
      if (g.typeIds.length === 0) continue;
      sum += Math.round((g.instanceCount * hidden) / g.typeIds.length);
    }
    return sum;
  }, [allGroups, hiddenCountByGroup]);

  const hideAll = () => {
    const next = new Set<string>();
    for (const g of allGroups) for (const id of g.typeIds) next.add(id);
    setHiddenTypeIds(next);
  };
  const showAll = () => clearHiddenTypeIds();

  /** Toggle every typeId in a group together. Mixed → all hidden; all hidden → all visible. */
  const toggleGroup = (group: TypeGroupStat) => {
    const next = new Set(hiddenTypeIds);
    const allHidden = group.typeIds.every((id) => next.has(id));
    if (allHidden) {
      for (const id of group.typeIds) next.delete(id);
    } else {
      for (const id of group.typeIds) next.add(id);
    }
    setHiddenTypeIds(next);
  };

  if (allGroups.length === 0) return null;

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
          <span>Filter by Type</span>
        </div>
        <span className="text-zinc-400 font-mono">
          {totalInstances - hiddenInstances}/{totalInstances}
        </span>
      </button>

      {!collapsed && (
        <div className="border-t border-zinc-700/60 flex flex-col min-h-0 flex-1">
          <div className="px-3 py-2 flex items-center gap-2 flex-shrink-0">
            <input
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search types…"
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
            {hiddenInstances > 0 && (
              <span className="ml-auto text-zinc-400 text-[10px]">
                {hiddenInstances.toLocaleString()} hidden
              </span>
            )}
          </div>

          {/* Feature #5386 — sort selector. Persisted per model via uiStore. */}
          <div className="px-3 pb-2 flex items-center gap-2 flex-shrink-0">
            <label htmlFor="type-filter-sort" className="text-[10px] text-zinc-400 uppercase tracking-wide">
              Sort
            </label>
            <select
              id="type-filter-sort"
              aria-label="Sort types"
              value={sortOrder}
              onChange={(e) => setSortOrder(e.target.value as SortOrder)}
              className="flex-1 bg-zinc-900/60 border border-zinc-700 rounded px-2 py-1 text-xs text-zinc-200 focus:outline-none focus:border-zinc-500"
            >
              <option value="count-desc">Count (high to low)</option>
              <option value="count-asc">Count (low to high)</option>
              <option value="name-asc">Name (A → Z)</option>
              <option value="name-desc">Name (Z → A)</option>
            </select>
          </div>

          <ul className="flex-1 min-h-0 overflow-y-auto border-t border-zinc-700/60 divide-y divide-zinc-700/40">
            {filtered.length === 0 ? (
              <li className="px-3 py-2 text-zinc-500 italic">No matching types.</li>
            ) : (
              filtered.map((g) => {
                const hidden = hiddenCountByGroup.get(g.name) ?? 0;
                const allHidden = hidden === g.typeIds.length;
                const indeterminate = hidden > 0 && !allHidden;
                return (
                  <li key={g.name}>
                    <label className="flex items-center justify-between gap-2 px-3 py-1.5 hover:bg-zinc-700/30 cursor-pointer">
                      <div className="flex items-center gap-2 flex-1 min-w-0">
                        <input
                          type="checkbox"
                          checked={!allHidden}
                          ref={(el) => { if (el) el.indeterminate = indeterminate; }}
                          onChange={() => toggleGroup(g)}
                          className="accent-violet-500"
                        />
                        <span className="truncate text-zinc-200">
                          {g.name}
                        </span>
                      </div>
                      <span className="text-[10px] font-mono text-zinc-400">{g.instanceCount.toLocaleString()}</span>
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
