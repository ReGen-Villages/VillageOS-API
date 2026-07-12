import type { KpiWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { asNumber, asSeries } from '../../../api/dashboardApi';
import { useBinding } from '../../../hooks/useDashboard';
import { WidgetCard } from './WidgetCard';
import { Sparkline } from './Sparkline';
import { formatNumber, formatDelta, deltaTone } from './format';

export function KpiCard({ widget, ctx }: { widget: KpiWidget; ctx: ResolveContext }) {
  const value = useBinding(widget.value, ctx);
  const delta = useBinding(widget.delta, ctx);
  const spark = useBinding(widget.spark, ctx);

  const v = asNumber(value.value);
  const d = asNumber(delta.value);
  const series = asSeries(spark.value);
  const direction = widget.direction ?? 'up-good';

  const onTarget =
    widget.target === undefined || v === null
      ? null
      : direction === 'down-good'
        ? v <= widget.target
        : v >= widget.target;
  const tone = deltaTone(d, direction);

  return (
    <WidgetCard
      title={widget.title}
      right={
        onTarget !== null && (
          <span
            className={`text-[10px] font-bold px-2 py-0.5 rounded-full whitespace-nowrap ${
              onTarget
                ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400'
                : 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400'
            }`}
          >
            {onTarget ? 'on target' : 'watch'}
          </span>
        )
      }
    >
      <div className="flex items-end justify-between gap-3">
        <div>
          <div className="text-3xl font-bold tracking-tight tabular-nums text-zinc-900 dark:text-white leading-none mt-2 mb-1">
            {value.loading ? <span className="text-zinc-300 dark:text-zinc-600">···</span> : formatNumber(v, widget.format)}
            {widget.unit && <span className="text-sm text-zinc-400 dark:text-zinc-500 font-semibold ml-1">{widget.unit}</span>}
          </div>
          {d !== null && (
            <div
              className={`text-xs font-bold inline-flex items-center gap-1 ${
                tone === 'up'
                  ? 'text-emerald-600 dark:text-emerald-400'
                  : tone === 'down'
                    ? 'text-red-600 dark:text-red-400'
                    : 'text-zinc-400'
              }`}
            >
              {formatDelta(d, widget.format)}
            </div>
          )}
        </div>
        {series.length >= 2 && <Sparkline values={series} />}
      </div>
      {(widget.targetLabel || widget.footnote) && (
        <div className="text-[11px] text-zinc-400 dark:text-zinc-500 mt-1.5">
          {[widget.targetLabel, widget.footnote].filter(Boolean).join(' · ')}
        </div>
      )}
    </WidgetCard>
  );
}
