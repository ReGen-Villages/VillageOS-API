import { useTranslation } from 'react-i18next';
import type { VosThing, VosRelationship } from '../../types/vos';

interface Props {
  things: VosThing[];
  relationships: VosRelationship[];
  /** False until the store holds the whole model. What it holds before that is one page's own set
   *  or nothing, and the card then says the model is being read instead of counting it — a count
   *  would read as the size of the model. */
  wholeModelHeld: boolean;
}

interface ModelFigures {
  things: number;
  relationships: number;
  predicates: number;
  properties: number;
  handlers: number;
  topPredicates: [name: string, count: number][];
}

function figuresOf(things: VosThing[], relationships: VosRelationship[]): ModelFigures {
  // Top predicates by usage, looked up through a Map so the cost does not grow with things times relationships
  const thingMap = new Map(things.map((t) => [t.Id, t]));
  const predicateCounts = new Map<string, number>();
  for (const r of relationships) {
    const name = thingMap.get(r.PredicateId)?.Name || r.PredicateId;
    predicateCounts.set(name, (predicateCounts.get(name) || 0) + 1);
  }
  return {
    things: things.length,
    relationships: relationships.length,
    predicates: new Set(relationships.map((r) => r.PredicateId)).size,
    properties: things.reduce((sum, t) => sum + (t.Properties ? Object.keys(t.Properties).length : 0), 0),
    handlers: things.filter((t) => t.Properties && 'ExecutablePath' in t.Properties).length,
    topPredicates: [...predicateCounts.entries()].sort((a, b) => b[1] - a[1]).slice(0, 5),
  };
}

export function ModelStatisticsCard({ things, relationships, wholeModelHeld }: Props) {
  const { t } = useTranslation();
  const figures = wholeModelHeld ? figuresOf(things, relationships) : null;

  return (
    <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4" aria-busy={!wholeModelHeld}>
      <div className="flex items-baseline justify-between mb-3">
        <h3 className="text-sm font-semibold text-zinc-500 dark:text-zinc-400">{t('dashboard.statistics.title')}</h3>
        {!figures && <span className="text-xs text-zinc-400">{t('statement.reading')}</span>}
      </div>
      <div className="grid grid-cols-2 gap-3">
        <Stat label={t('dashboard.statistics.things')} value={figures?.things} />
        <Stat label={t('dashboard.statistics.relationships')} value={figures?.relationships} />
        <Stat label={t('dashboard.statistics.predicates')} value={figures?.predicates} />
        <Stat label={t('dashboard.statistics.properties')} value={figures?.properties} />
        <Stat label={t('dashboard.statistics.handlers')} value={figures?.handlers} />
      </div>
      {figures && figures.topPredicates.length > 0 && (
        <div className="mt-3 pt-3 border-t border-zinc-200 dark:border-zinc-700">
          <h4 className="text-xs text-zinc-500 mb-1">{t('dashboard.statistics.topPredicates')}</h4>
          {figures.topPredicates.map(([name, count]) => (
            <div key={name} className="flex justify-between text-xs py-0.5">
              <span className="text-zinc-400">{name}</span>
              <span className="font-mono">{count}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** A figure not yet known keeps its place as a dash, so the card does not change size when the
 *  model arrives. */
function Stat({ label, value }: { label: string; value: number | undefined }) {
  return (
    <div>
      <div className="text-2xl font-bold text-zinc-900 dark:text-white">{value ?? '—'}</div>
      <div className="text-xs text-zinc-500">{label}</div>
    </div>
  );
}
