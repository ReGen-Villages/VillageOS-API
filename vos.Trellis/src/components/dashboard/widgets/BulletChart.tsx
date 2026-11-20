import { useTranslation } from 'react-i18next';
import type { BulletWidget, BulletRow } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';
import { asNumber } from '@tendril-ui/tendril';
import { useBindings } from '@tendril-ui/tendril';
import { WidgetCard } from './WidgetCard';
import { formatNumber } from './format';

/** Each row's track runs from 0 to `max`, by default 1, for a fraction; target and band are in the row's units.
 *  'down-good', the default, treats passing the band as critical, and 'up-good' falling below it. */
export function BulletChart({ widget, context }: { widget: BulletWidget; context: ResolveContext }) {
  const { t } = useTranslation();
  const results = useBindings(
    widget.rows.map((r) => r.value),
    context,
  );

  return (
    <WidgetCard title={widget.title}>
      <div className="mt-3">
        {widget.rows.map((row, i) => (
          <BulletBar key={row.label} row={row} value={asNumber(results[i].value)} />
        ))}
      </div>
      <div className="flex flex-wrap gap-3 mt-3 text-[11px] text-zinc-500 dark:text-zinc-400">
        <span className="inline-flex items-center gap-1.5"><i className="inline-block w-0.5 h-3.5 bg-zinc-800 dark:bg-zinc-200" />{t('widgets.bullet.target')}</span>
        <span className="inline-flex items-center gap-1.5"><i className="inline-block w-2.5 h-2.5 rounded-sm" style={{ background: 'color-mix(in srgb, var(--good) 28%, transparent)' }} />{t('widgets.bullet.healthyBand')}</span>
        <span className="inline-flex items-center gap-1.5"><i className="inline-block w-2.5 h-2.5 rounded-sm" style={{ background: 'var(--warn)' }} />{t('widgets.bullet.warning')}</span>
        <span className="inline-flex items-center gap-1.5"><i className="inline-block w-2.5 h-2.5 rounded-sm" style={{ background: 'var(--crit)' }} />{t('widgets.bullet.critical')}</span>
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
  // The band's width comes from two clamped positions rather than a scaled difference, so a band past `max`
  // cannot run over the labels.
  const position = (x: number) => Math.max(0, Math.min(100, (x / max) * 100));
  const percent = (x: number) => `${position(x)}%`;

  return (
    <div className="grid items-center gap-3 my-2.5" style={{ gridTemplateColumns: '104px 1fr 56px' }}>
      <div className="text-[12px] text-zinc-600 dark:text-zinc-300">{row.label}</div>
      <div className="relative h-[15px] bg-zinc-100 dark:bg-zinc-700 rounded">
        {band && (
          <div
            className="absolute top-0 h-[15px] rounded"
            style={{ left: `${position(band[0])}%`, width: `${position(band[1]) - position(band[0])}%`, background: 'color-mix(in srgb, var(--good) 14%, transparent)' }}
          />
        )}
        <div className="absolute top-0 left-0 h-[15px] rounded" style={{ width: percent(v), background: color }} />
        {row.target !== undefined && (
          <div className="absolute -top-[3px] h-[21px] w-0.5 bg-zinc-800 dark:bg-zinc-200 rounded-sm" style={{ left: percent(row.target) }} />
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
