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
 * A type is any Thing that another Thing's `is` points to. The graph and model pages both read
 * `hiddenTypeIds`, so a toggle hides the instances on both.
 *
 * Rows are grouped by name, because names are not unique and a person reads types that share one as one
 * kind. A group whose types are partly hidden shows as indeterminate.
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
      // Only the group's total is known here, so each hidden type is counted as an equal share of it. That is
      // close enough for the header count; an exact count would need the per-type counts kept beside the groups.
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
