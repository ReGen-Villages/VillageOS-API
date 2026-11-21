import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FilterPanel, FilterRow } from './FilterPanel';
import { useUiStore } from '../../stores/uiStore';
import type { PredicateStatistics } from '../../utils/predicateCluster';

/** Works as the type filter does. `hiddenPredicateIds` is kept apart from `activePredicateIds`, which drives
 *  clustering from the radial menu for a different purpose. */
export function PredicateFilterPanel() {
  const { t } = useTranslation();
  const predicateStatistics = useUiStore((s) => s.predicateStatistics);
  const hiddenPredicateIds = useUiStore((s) => s.hiddenPredicateIds);
  const toggleHiddenPredicate = useUiStore((s) => s.toggleHiddenPredicate);
  const setHiddenPredicateIds = useUiStore((s) => s.setHiddenPredicateIds);
  const clearHiddenPredicateIds = useUiStore((s) => s.clearHiddenPredicateIds);

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
    <FilterPanel
      title={t('graph.predicateFilter.title')}
      shown={visibleEdges}
      total={totalEdges}
      controls={{
        search, onSearch: setSearch, placeholder: t('graph.predicateFilter.search'),
        onShowAll: showAll, onHideAll: hideAll, hidden: hiddenEdges,
      }}
      noMatch={filtered.length === 0 ? t('graph.predicateFilter.noMatch') : null}
    >
      {filtered.map((s) => (
        <FilterRow
          key={s.predicateId}
          checked={!hiddenPredicateIds.has(s.predicateId)}
          onToggle={() => toggleHiddenPredicate(s.predicateId)}
          swatch={s.color}
          name={s.predicateName}
          count={s.edgeCount}
        />
      ))}
    </FilterPanel>
  );
}
