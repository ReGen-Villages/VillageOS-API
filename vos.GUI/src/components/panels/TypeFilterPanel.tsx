import { useMemo, useState } from 'react';
import { ChevronDown, ChevronRight, Eye, EyeOff, X } from 'lucide-react';
import { useModelStore } from '../../stores/modelStore';
import { useUiStore } from '../../stores/uiStore';
import { discoverTypes, type TypeStat } from '../../utils/typeFilter';

/**
 * Feature #5362 — shared type-filter panel rendered inside both the Graph
 * page and the Model page sidebars. Lists every type Thing in the loaded
 * model (any Thing that's the target of an `is` relationship from another
 * Thing) with an instance count and a checkbox. Toggling immediately drops
 * those instances from BOTH visualizations because both consume
 * `useUiStore.hiddenTypeIds`.
 *
 * Domain-agnostic — no string-literal references to IFC class names.
 */
export function TypeFilterPanel() {
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const hiddenTypeIds = useUiStore((s) => s.hiddenTypeIds);
  const toggleHiddenType = useUiStore((s) => s.toggleHiddenType);
  const setHiddenTypeIds = useUiStore((s) => s.setHiddenTypeIds);
  const clearHiddenTypeIds = useUiStore((s) => s.clearHiddenTypeIds);

  const [collapsed, setCollapsed] = useState(false);
  const [search, setSearch] = useState('');

  const allTypes = useMemo<TypeStat[]>(
    () => discoverTypes(things, relationships),
    [things, relationships],
  );

  const filtered = useMemo(() => {
    if (!search) return allTypes;
    const q = search.toLowerCase();
    return allTypes.filter((t) => t.name.toLowerCase().includes(q));
  }, [allTypes, search]);

  const totalInstances = useMemo(
    () => allTypes.reduce((sum, t) => sum + t.instanceCount, 0),
    [allTypes],
  );

  const hiddenInstances = useMemo(() => {
    let sum = 0;
    for (const t of allTypes) if (hiddenTypeIds.has(t.typeId)) sum += t.instanceCount;
    return sum;
  }, [allTypes, hiddenTypeIds]);

  const hideAll = () => setHiddenTypeIds(new Set(allTypes.map((t) => t.typeId)));
  const showAll = () => clearHiddenTypeIds();

  if (allTypes.length === 0) return null;

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
          <span>Filter by Type</span>
        </div>
        <span className="text-zinc-400 font-mono">
          {totalInstances - hiddenInstances}/{totalInstances}
        </span>
      </button>

      {!collapsed && (
        <div className="border-t border-zinc-700/60">
          <div className="px-3 py-2 flex items-center gap-2">
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

          <div className="px-3 pb-2 flex items-center gap-2">
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

          <ul className="max-h-[40vh] overflow-y-auto border-t border-zinc-700/60 divide-y divide-zinc-700/40">
            {filtered.length === 0 ? (
              <li className="px-3 py-2 text-zinc-500 italic">No matching types.</li>
            ) : (
              filtered.map((t) => {
                const visible = !hiddenTypeIds.has(t.typeId);
                return (
                  <li key={t.typeId}>
                    <label className="flex items-center justify-between gap-2 px-3 py-1.5 hover:bg-zinc-700/30 cursor-pointer">
                      <div className="flex items-center gap-2 flex-1 min-w-0">
                        <input
                          type="checkbox"
                          checked={visible}
                          onChange={() => toggleHiddenType(t.typeId)}
                          className="accent-violet-500"
                        />
                        <span className="truncate text-zinc-200">
                          {t.name}
                        </span>
                      </div>
                      <span className="text-[10px] font-mono text-zinc-400">{t.instanceCount.toLocaleString()}</span>
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
