import { useMemo } from 'react';
import type { LeaderboardWidget, LeaderMetric } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';
import { asRows } from '../../../api/dashboardApi';
import { useBinding } from '../../../hooks/useDashboard';
import { WidgetCard } from './WidgetCard';
import { formatNumber } from './format';

/** Normalise a metric value to 0..1 given its best/worst anchors + direction. */
function normalise(v: number, m: LeaderMetric): number {
  const best = m.best ?? 100;
  const worst = m.worst ?? 0;
  const span = best - worst || 1;
  const raw = (v - worst) / span;
  const n = m.direction === 'down-good' ? 1 - raw : raw;
  return Math.max(0, Math.min(1, n));
}

function score(row: Row, metrics: LeaderMetric[]): number {
  let total = 0;
  let wsum = 0;
  for (const m of metrics) {
    if (!m.weight) continue;
    total += normalise(Number(row[m.key]) || 0, m) * m.weight;
    wsum += m.weight;
  }
  return wsum ? Math.round((total / wsum) * 1000) / 10 : 0;
}

/** Site/entity scorecard: ranks compare-entities by a weighted score across metrics. */
export function Leaderboard({ widget, ctx }: { widget: LeaderboardWidget; ctx: ResolveContext }) {
  const { loading, value } = useBinding(widget.entities, ctx);
  const rows = asRows(value);
  const labelKey = widget.labelKey ?? 'name';

  const ranked = useMemo(() => {
    const scored: (Row & { __score: number })[] = rows.map((r) => ({ ...r, __score: score(r, widget.metrics) }));
    scored.sort((a, b) => b.__score - a.__score);
    return scored;
  }, [rows, widget.metrics]);
  const maxScore = Math.max(1, ...ranked.map((r) => r.__score));

  return (
    <WidgetCard title={widget.title} hint={widget.hint}>
      {loading ? (
        <div className="py-6 text-center text-xs text-zinc-400">Loading…</div>
      ) : !ranked.length ? (
        <div className="py-6 text-center text-xs text-zinc-400">No entities to compare.</div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-[12.5px] mt-2">
            <thead>
              <tr>
                <th className="text-left px-2.5 py-2 border-b border-zinc-200 dark:border-zinc-700 text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400 font-semibold">
                  {widget.labelKey ? '' : 'Entity'}
                </th>
                {widget.metrics.map((m) => (
                  <th key={m.key} className="text-right px-2.5 py-2 border-b border-zinc-200 dark:border-zinc-700 text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400 font-semibold">
                    {m.label}
                  </th>
                ))}
                <th className="text-right px-2.5 py-2 border-b border-zinc-200 dark:border-zinc-700 text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400 font-semibold">
                  Score
                </th>
              </tr>
            </thead>
            <tbody>
              {ranked.map((r, i) => (
                <tr key={(r.id as string) ?? i}>
                  <td className="px-2.5 py-2.5 border-b border-zinc-100 dark:border-zinc-700/60">
                    <div className="font-bold flex items-center gap-2 text-zinc-800 dark:text-zinc-100">
                      <span className="w-4 inline-block">{i === 0 ? '🏆' : ''}</span>
                      {String(r[labelKey] ?? r.name ?? '')}
                      {widget.sublabelKey && r[widget.sublabelKey] !== undefined && (
                        <span className="font-normal text-zinc-400 dark:text-zinc-500 text-[11.5px]">{String(r[widget.sublabelKey])}</span>
                      )}
                    </div>
                  </td>
                  {widget.metrics.map((m) => (
                    <td key={m.key} className="px-2.5 py-2.5 border-b border-zinc-100 dark:border-zinc-700/60 text-right tabular-nums">
                      {formatNumber(Number(r[m.key]), m.format)}
                    </td>
                  ))}
                  <td className="px-2.5 py-2.5 border-b border-zinc-100 dark:border-zinc-700/60 text-right tabular-nums">
                    <span className="font-bold">{(r.__score as number).toFixed(1)}</span>
                    <span className="inline-block w-[54px] h-1.5 rounded-full bg-zinc-100 dark:bg-zinc-700 relative align-middle ml-2">
                      <i
                        className="absolute left-0 top-0 h-1.5 rounded-full"
                        style={{ width: `${((r.__score as number) / maxScore) * 100}%`, background: i === 0 ? 'var(--good)' : 'var(--spark)' }}
                      />
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </WidgetCard>
  );
}
