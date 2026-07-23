import type { BulletWidget, BulletRow } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { asNumber } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { WidgetCard } from './WidgetCard';
import { formatNumber } from './format';

/**
 * Bullet-bar chart (actual vs. target band). Each row's track runs 0..`max` (default 1, the
 * utilization case where the value is a 0..1 fraction); target and band are positions on that
 * track in the row's own units. `direction` sets which way is good — 'down-good' (default) treats
 * overshooting the band as critical, 'up-good' treats falling below it as critical.
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
        <span className="inline-flex items-center gap-1.5"><i className="inline-block w-2.5 h-2.5 rounded-sm" style={{ background: 'var(--warn)' }} />warning</span>
        <span className="inline-flex items-center gap-1.5"><i className="inline-block w-2.5 h-2.5 rounded-sm" style={{ background: 'var(--crit)' }} />critical</span>
      </div>
    </WidgetCard>
  );
}

function BulletBar({ row, value }: { row: BulletRow; value: number | null }) {
  const v = value ?? 0;
  const max = row.max ?? 1;
  const band = row.band;
  const target = row.target;
  const color = row.direction === 'up-good' ? upGoodColor(v, target, band) : downGoodColor(v, target, band);
  // Position on the 0..max track, clamped to the track edges. Deriving the band's width from two
  // clamped positions (not a scaled delta) keeps it inside the track — a band whose upper bound
  // exceeds max can never bleed past the right edge into the labels.
  const pos = (x: number) => Math.max(0, Math.min(100, (x / max) * 100));
  const pct = (x: number) => `${pos(x)}%`;

  return (
    <div className="grid items-center gap-3 my-2.5" style={{ gridTemplateColumns: '104px 1fr 56px' }}>
      <div className="text-[12px] text-zinc-600 dark:text-zinc-300">{row.label}</div>
      <div className="relative h-[15px] bg-zinc-100 dark:bg-zinc-700 rounded">
        {band && (
          <div
            className="absolute top-0 h-[15px] rounded"
            style={{ left: `${pos(band[0])}%`, width: `${pos(band[1]) - pos(band[0])}%`, background: 'color-mix(in srgb, var(--good) 14%, transparent)' }}
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

/** Utilization tone: at/below target is good, into the band is a warning, past it is critical. */
function downGoodColor(v: number, target: number | undefined, band: [number, number] | undefined): string {
  const over = band ? v > band[1] : false;
  const warn = !over && target !== undefined && v > target;
  return over ? 'var(--crit)' : warn ? 'var(--warn)' : 'var(--good)';
}

/** Self-sufficiency tone: at/above target is good, below the band is critical, in between a warning. */
function upGoodColor(v: number, target: number | undefined, band: [number, number] | undefined): string {
  const under = band ? v < band[0] : target !== undefined ? v < target : false;
  const warn = !under && target !== undefined && v < target;
  return under ? 'var(--crit)' : warn ? 'var(--warn)' : 'var(--good)';
}
