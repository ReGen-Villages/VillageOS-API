import type { BulletWidget, BulletRow } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { asNumber } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { WidgetCard } from './WidgetCard';
import { formatNumber } from './format';

/**
 * Bullet-bar chart (actual vs. target band). Values are 0..1 fractions unless a
 * row's format implies otherwise; band/target are 0..1 positions on the track.
 */
export function BulletChart({ widget, ctx }: { widget: BulletWidget; ctx: ResolveContext }) {
  const results = useBindings(
    widget.rows.map((r) => r.value),
    ctx,
  );

  return (
    <WidgetCard title={widget.title}>
      <div className="mt-3">
        {widget.rows.map((row, i) => (
          <BulletBar key={row.label} row={row} value={asNumber(results[i].value)} />
        ))}
      </div>
      <div className="flex flex-wrap gap-3 mt-3 text-[11px] text-zinc-500 dark:text-zinc-400">
        <span className="inline-flex items-center gap-1.5"><i className="inline-block w-0.5 h-3.5 bg-zinc-800 dark:bg-zinc-200" />target</span>
        <span className="inline-flex items-center gap-1.5"><i className="inline-block w-2.5 h-2.5 rounded-sm" style={{ background: 'color-mix(in srgb, var(--good) 28%, transparent)' }} />healthy band</span>
        <span className="inline-flex items-center gap-1.5"><i className="inline-block w-2.5 h-2.5 rounded-sm" style={{ background: 'var(--warn)' }} />tight</span>
        <span className="inline-flex items-center gap-1.5"><i className="inline-block w-2.5 h-2.5 rounded-sm" style={{ background: 'var(--crit)' }} />over</span>
      </div>
    </WidgetCard>
  );
}

function BulletBar({ row, value }: { row: BulletRow; value: number | null }) {
  const v = value ?? 0;
  const band = row.band;
  const over = band ? v > band[1] : false;
  const tight = band ? v > (row.target ?? band[1]) && !over : false;
  const color = over ? 'var(--crit)' : tight ? 'var(--warn)' : 'var(--good)';
  const pct = (x: number) => `${Math.max(0, Math.min(100, x * 100))}%`;

  return (
    <div className="grid items-center gap-3 my-2.5" style={{ gridTemplateColumns: '104px 1fr 56px' }}>
      <div className="text-[12px] text-zinc-600 dark:text-zinc-300">{row.label}</div>
      <div className="relative h-[15px] bg-zinc-100 dark:bg-zinc-700 rounded">
        {band && (
          <div
            className="absolute top-0 h-[15px] rounded"
            style={{ left: pct(band[0]), width: pct(band[1] - band[0]), background: 'color-mix(in srgb, var(--good) 14%, transparent)' }}
          />
        )}
        <div className="absolute top-0 left-0 h-[15px] rounded" style={{ width: pct(v), background: color }} />
        {row.target !== undefined && (
          <div className="absolute -top-[3px] h-[21px] w-0.5 bg-zinc-800 dark:bg-zinc-200 rounded-sm" style={{ left: pct(row.target) }} />
        )}
      </div>
      <div className="text-[12.5px] font-bold text-right tabular-nums" style={{ color }}>
        {value === null ? '—' : formatNumber(v, row.format ?? 'percent')}
      </div>
    </div>
  );
}
