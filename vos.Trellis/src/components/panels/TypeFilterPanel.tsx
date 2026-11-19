import { useCallback, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FilterPanel, FilterRow } from './FilterPanel';
import { useModelStore } from '../../stores/modelStore';
import { useUiStore } from '../../stores/uiStore';
import {
  discoverTypes,
  groupTypesByName,
  sortTypeGroups,
  NO_TYPE_ID,
  type SortOrder,
  type TypeGroupStat,
} from '../../utils/typeFilter';

/**
 * Shared type-filter panel rendered inside both the Graph
 * page and the Model page sidebars. Lists every type Thing in the loaded
 * model (any Thing that's the target of an `is` relationship from another
 * Thing) with an instance count and a checkbox. Toggling immediately drops
 * those instances from BOTH visualizations because both consume
 * `useUiStore.hiddenTypeIds`.
 *
 * Rows are grouped by Name. Multiple type-Things may legitimately
 * share a Name (Name is a display label, not a unique key); the user thinks
 * of them as one category. The row's checkbox controls every underlying
 * typeId in the group together; mixed underlying state renders as
 * indeterminate and normalizes to "all hidden" or "all visible" on click.
 *
 * Domain-agnostic — no string-literal references to IFC class names.
 */
export function TypeFilterPanel() {
  const { t } = useTranslation();
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const hiddenTypeIds = useUiStore((s) => s.hiddenTypeIds);
  const setHiddenTypeIds = useUiStore((s) => s.setHiddenTypeIds);
  const clearHiddenTypeIds = useUiStore((s) => s.clearHiddenTypeIds);
  const sortOrder = useUiStore((s) => s.typeSortOrder);
  const setSortOrder = useUiStore((s) => s.setTypeSortOrder);

  const [search, setSearch] = useState('');

  const allGroups = useMemo<TypeGroupStat[]>(
    () => sortTypeGroups(groupTypesByName(discoverTypes(things, relationships)), sortOrder),
    [things, relationships, sortOrder],
  );

  const shownName = useCallback(
    (g: TypeGroupStat) => (g.name === NO_TYPE_ID ? t('graph.typeFilter.noType') : g.name),
    [t],
  );

  const filtered = useMemo(() => {
    if (!search) return allGroups;
    const q = search.toLowerCase();
    return allGroups.filter((g) => shownName(g).toLowerCase().includes(q));
  }, [allGroups, search, shownName]);

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
    <FilterPanel
      title={t('graph.typeFilter.title')}
      shown={totalInstances - hiddenInstances}
      total={totalInstances}
      controls={{
        search, onSearch: setSearch, placeholder: t('graph.typeFilter.search'),
        onShowAll: showAll, onHideAll: hideAll, hidden: hiddenInstances,
      }}
      noMatch={filtered.length === 0 ? t('graph.typeFilter.noMatch') : null}
      beforeList={
        <div className="px-3 pb-2 flex items-center gap-2 flex-shrink-0">
          <label htmlFor="type-filter-sort" className="text-[10px] text-zinc-400 uppercase tracking-wide">
            {t('graph.typeFilter.sort')}
          </label>
          <select
            id="type-filter-sort"
            aria-label={t('graph.typeFilter.sortAria')}
            value={sortOrder}
            onChange={(e) => setSortOrder(e.target.value as SortOrder)}
            className="flex-1 bg-zinc-900/60 border border-zinc-700 rounded px-2 py-1 text-xs text-zinc-200 focus:outline-none focus:border-zinc-500"
          >
            <option value="count-desc">{t('graph.typeFilter.sortCountDescending')}</option>
            <option value="count-asc">{t('graph.typeFilter.sortCountAsc')}</option>
            <option value="name-asc">{t('graph.typeFilter.sortNameAsc')}</option>
            <option value="name-desc">{t('graph.typeFilter.sortNameDescending')}</option>
          </select>
        </div>
      }
    >
      {filtered.map((g) => {
        const hidden = hiddenCountByGroup.get(g.name) ?? 0;
        const allHidden = hidden === g.typeIds.length;
        return (
          <FilterRow
            key={g.name}
            checked={!allHidden}
            indeterminate={hidden > 0 && !allHidden}
            onToggle={() => toggleGroup(g)}
            name={shownName(g)}
            count={g.instanceCount}
          />
        );
      })}
    </FilterPanel>
  );
}
