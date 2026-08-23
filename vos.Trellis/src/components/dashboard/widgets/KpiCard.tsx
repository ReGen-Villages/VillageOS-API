import { useTranslation } from 'react-i18next';
import type { KpiWidget } from '../../../types/dashboard';
import { ORIGIN_KINDS, type OriginKind } from '../../../types/vos';
import type { ResolveContext, Row } from '../../../api/dashboardApi';
import { asNumber, asRows, asSeries } from '../../../api/dashboardApi';
import { useBinding } from '../../../hooks/useDashboard';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { Sparkline } from './Sparkline';
import { formatNumber, formatDelta, deltaTone, originTone } from './format';
import { originSentence } from './originSentence';

/** Width to draw at before the card has been measured, and the floor a narrow card still gets. */
const MINIMUM_SPARK_WIDTH = 108;

export function KpiCard({ widget, ctx }: { widget: KpiWidget; ctx: ResolveContext }) {
  const { t } = useTranslation();
  const value = useBinding(widget.value, ctx);
  const delta = useBinding(widget.delta, ctx);
  const spark = useBinding(widget.spark, ctx);
  const sparkBaseline = useBinding(widget.sparkBaseline, ctx);
  const origin = useBinding(widget.origin, ctx);

  const v = asNumber(value.value);
  const d = asNumber(delta.value);
  const series = asSeries(spark.value);
  const baseline = asNumber(sparkBaseline.value);
  const [sparkArea, measuredWidth] = useElementWidth();
  const sparkWidth = Math.max(measuredWidth, MINIMUM_SPARK_WIDTH);
  const peak = series.length ? Math.max(...series) : null;
  const trough = series.length ? Math.min(...series) : null;
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
            {onTarget ? t('widgets.kpi.onTarget') : t('widgets.kpi.watch')}
          </span>
        )
      }
    >
      <div className="flex items-end gap-6">
        <div className="flex-shrink-0">
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
        {series.length >= 2 && (
          <div ref={sparkArea} className="flex-1 min-w-0">
            <Sparkline values={series} baseline={baseline} width={sparkWidth} height={48} />
            <div className="mt-0.5 flex items-center justify-between gap-3 flex-wrap text-[10px] text-zinc-400 dark:text-zinc-500 tabular-nums">
              <span>
                {t('widgets.kpi.peak')} {formatNumber(peak, widget.format)} · {t('widgets.kpi.trough')} {formatNumber(trough, widget.format)}
                {widget.unit && ` ${widget.unit}`}
              </span>
              {baseline !== null && widget.sparkBaselineLabel && (
                <span className="flex items-center gap-1">
                  <span className="inline-block w-3 border-t border-dashed border-current" />
                  {widget.sparkBaselineLabel} {formatNumber(baseline, widget.format)}
                </span>
              )}
            </div>
          </div>
        )}
      </div>
      {(widget.targetLabel || widget.footnote) && (
        <div className="text-[11px] text-zinc-400 dark:text-zinc-500 mt-1.5">
          {[widget.targetLabel, widget.footnote].filter(Boolean).join(' · ')}
        </div>
      )}
      <OriginLines rows={asRows(origin.value)} />
    </WidgetCard>
  );
}

/**
 * Where the figure came from, under it. One line per Thing the binding read, so a path reaching
 * several says it of each.
 *
 * Every word is the model's. A row carrying no origin — which is what a slot bound to something
 * other than an `origin` binding resolves to — and an origin the spec left unworded are both left
 * unsaid rather than drawn as a blank line: Trellis has no wording of its own to put there, and a
 * placeholder would read as an answer.
 */
function OriginLines({ rows }: { rows: Row[] }) {
  const lines = rows
    .filter((row) => ORIGIN_KINDS.includes(row.origin as OriginKind) && typeof row.reads === 'string')
    .map((row, i) => ({
      key: `${String(row.origin)}-${i}`,
      origin: row.origin as OriginKind,
      sentence: originSentence(row.reads as string, {
        source: typeof row.source === 'string' ? row.source : null,
        resolvedAt: typeof row.resolvedAt === 'string' ? row.resolvedAt : null,
      }),
    }))
    .filter((line) => line.sentence !== '');

  return lines.map((line) => (
    <div key={line.key} className={`text-[11px] mt-1.5 ${originTone(line.origin)}`}>
      {line.sentence}
    </div>
  ));
}
