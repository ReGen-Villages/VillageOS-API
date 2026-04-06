import type { VosThing, VosRelationship } from '../../types/vos';

interface Props {
  things: VosThing[];
  relationships: VosRelationship[];
}

export function ModelStatsCard({ things, relationships }: Props) {
  const predicateIds = new Set(relationships.map((r) => r.PredicateId));
  const totalProperties = things.reduce((sum, t) => sum + (t.Properties ? Object.keys(t.Properties).length : 0), 0);
  const handlers = things.filter((t) => t.Properties && 'ExecutablePath' in t.Properties);

  // Top predicates by usage — use Map lookup to avoid O(things × relationships)
  const thingMap = new Map(things.map((t) => [t.Id, t]));
  const predCounts = new Map<string, number>();
  for (const r of relationships) {
    const name = thingMap.get(r.PredicateId)?.Name || r.PredicateId;
    predCounts.set(name, (predCounts.get(name) || 0) + 1);
  }
  const topPredicates = [...predCounts.entries()].sort((a, b) => b[1] - a[1]).slice(0, 5);

  return (
    <div className="bg-white dark:bg-zinc-800 rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
      <h3 className="text-sm font-semibold text-zinc-500 dark:text-zinc-400 mb-3">Model Statistics</h3>
      <div className="grid grid-cols-2 gap-3">
        <Stat label="Things" value={things.length} />
        <Stat label="Relationships" value={relationships.length} />
        <Stat label="Predicates" value={predicateIds.size} />
        <Stat label="Properties" value={totalProperties} />
        <Stat label="Handlers" value={handlers.length} />
      </div>
      {topPredicates.length > 0 && (
        <div className="mt-3 pt-3 border-t border-zinc-200 dark:border-zinc-700">
          <h4 className="text-xs text-zinc-500 mb-1">Top Predicates</h4>
          {topPredicates.map(([name, count]) => (
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

function Stat({ label, value }: { label: string; value: number }) {
  return (
    <div>
      <div className="text-2xl font-bold text-zinc-900 dark:text-white">{value}</div>
      <div className="text-xs text-zinc-500">{label}</div>
    </div>
  );
}
