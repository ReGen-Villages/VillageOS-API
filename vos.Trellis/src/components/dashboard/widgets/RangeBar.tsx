import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { RangeBarWidget, RangeSeries } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { AxisUnit, ChartBands, ChartTooltip, HistoryWindow, SeriesTable, ValueGridlines } from './chartChrome';
import { frameOf, PLOT_HEIGHT } from './chartFrame';
import { monthSlots } from './chartScale';
import { windowOf } from './historySeries';
import { formatNumber } from './format';
import { useMonthlyFigures, type Period } from './useMonthlyFigures';

/** The seven statistics in the order the legend, the tooltip and the table read them: top down. */
export const STATISTICS = [
  'recordedHigh', 'designHigh', 'averageHigh', 'mean', 'averageLow', 'designLow', 'recordedLow',
] as const satisfies readonly (keyof RangeSeries)[];
type Statistic = (typeof STATISTICS)[number];

/** The four stacked segments, top down, each between two statistics, on the diverging scale about
 *  the mean: the far arms deeper, the near arms paler. */
const SEGMENTS = [
  { name: 'designHigh', top: 'designHigh', bottom: 'averageHigh', fill: 'var(--chart-warm-far)' },
  { name: 'averageHigh', top: 'averageHigh', bottom: 'mean', fill: 'var(--chart-warm-near)' },
  { name: 'averageLow', top: 'mean', bottom: 'averageLow', fill: 'var(--chart-cool-near)' },
  { name: 'designLow', top: 'averageLow', bottom: 'designLow', fill: 'var(--chart-cool-far)' },
] as const;

const WIDEST_BAR = 24;
/** A thin ring in the surface colour is the gap between stacked segments and around the circles. */
const SURFACE_GAP = 2;
const MARKER_RADIUS = 4;

export function RangeBar({ widget, context }: { widget: RangeBarWidget; context: ResolveContext }) {
  const { t } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<number | null>(null);

  const { periods, bands, domain } = useMonthlyFigures(STATISTICS, widget, context);
  const { width, height, plotTop, plotBottom, plotLeft, plotRight, y, ticks } = frameOf(measuredWidth, domain);
  const { slotWidth, centreOf } = monthSlots(plotLeft, plotRight, !!widget.annual);
  const barWidth = Math.min(WIDEST_BAR, slotWidth * 0.6);
  const figure = (value: number | undefined) =>
    value === undefined ? '—' : `${formatNumber(value, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;
  const label = (statistic: Statistic) => t(`widgets.rangeBar.${statistic}`);
  const readOut = (period: Period<Statistic>) =>
    `${period.name}: ${STATISTICS.filter((statistic) => period.figures[statistic] !== undefined)
      .map((statistic) => `${label(statistic)} ${figure(period.figures[statistic])}`).join(', ')}`;
  const shown = active === null ? undefined : periods.find((period) => period.at === active);

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.months.mean)} />}>
      <div ref={measure} className="relative mt-2">
        <svg
          width={width}
          height={height}
          viewBox={`0 0 ${width} ${height}`}
          role="img"
          aria-label={widget.title ?? t('widgets.history.annual')}
          className="block max-w-full"
        >
          <ChartBands bands={bands} domain={domain} y={y} left={plotLeft} right={plotRight} />
          <ValueGridlines ticks={ticks} y={y} left={plotLeft} right={plotRight} />
          <AxisUnit unit={widget.unit} left={plotLeft} plotBottom={plotBottom} />
          {widget.annual && (
            <line
              x1={plotLeft + slotWidth * 12} x2={plotLeft + slotWidth * 12}
              y1={plotTop} y2={plotBottom} stroke="var(--chart-axis)" strokeWidth={1}
            />
          )}
          {periods.map((period) => {
            const centre = centreOf(period.at);
            const left = centre - barWidth / 2;
            const statistics = period.figures;
            return (
              <g
                key={period.key}
                data-month={period.key}
                role="img"
                tabIndex={0}
                aria-label={readOut(period)}
                onFocus={() => setActive(period.at)}
                onBlur={() => setActive(null)}
                onMouseEnter={() => setActive(period.at)}
                onMouseLeave={() => setActive(null)}
                className="outline-none"
              >
                <rect x={left - 6} y={plotTop} width={barWidth + 12} height={PLOT_HEIGHT} fill="transparent" />
                {SEGMENTS.map((segment) => {
                  const top = statistics[segment.top];
                  const bottom = statistics[segment.bottom];
                  if (top === undefined || bottom === undefined) return null;
                  return (
                    <rect
                      key={segment.name}
                      data-segment={segment.name}
                      x={left}
                      y={y(top)}
                      width={barWidth}
                      height={Math.max(0, y(bottom) - y(top))}
                      fill={segment.fill}
                      stroke="var(--card-bg)"
                      strokeWidth={SURFACE_GAP}
                    />
                  );
                })}
                {statistics.mean !== undefined && (
                  <line
                    data-mark="mean"
                    x1={left} x2={left + barWidth} y1={y(statistics.mean)} y2={y(statistics.mean)}
                    stroke="var(--chart-ink)" strokeWidth={2}
                  />
                )}
                {(['recordedHigh', 'recordedLow'] as const).map((statistic) =>
                  statistics[statistic] === undefined ? null : (
                    <circle
                      key={statistic}
                      data-mark={statistic}
                      cx={centre}
                      cy={y(statistics[statistic]!)}
                      r={MARKER_RADIUS}
                      fill="var(--card-bg)"
                      stroke="var(--chart-ink)"
                      strokeWidth={1.5}
                    />
                  ))}
                <text x={centre} y={plotBottom + 15} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)">
                  {period.name}
                </text>
                {active === period.at && (
                  <rect x={left - 3} y={plotTop} width={barWidth + 6} height={PLOT_HEIGHT} fill="none" stroke="var(--chart-ink-muted)" strokeWidth={1} rx={3} />
                )}
              </g>
            );
          })}
        </svg>
        {shown && (
          <ChartTooltip left={centreOf(shown.at)} top={plotTop} width={width} title={shown.name}>
            {STATISTICS.filter((statistic) => shown.figures[statistic] !== undefined).map((statistic) => (
              <div key={statistic} className="flex items-baseline gap-2">
                <StatisticKey statistic={statistic} />
                <span className="font-semibold tabular-nums">{figure(shown.figures[statistic])}</span>
                <span className="text-zinc-500 dark:text-zinc-400">{label(statistic)}</span>
              </div>
            ))}
          </ChartTooltip>
        )}
      </div>
      <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
        {STATISTICS.map((statistic) => (
          <li key={statistic} className="inline-flex items-center gap-1.5">
            <StatisticKey statistic={statistic} />
            {label(statistic)}
          </li>
        ))}
      </ul>
      <SeriesTable
        caption={widget.title ?? ''}
        columns={STATISTICS.map(label)}
        rows={periods.map((period) => ({
          name: period.name,
          cells: STATISTICS.map((statistic) => figure(period.figures[statistic])),
        }))}
      />
    </WidgetCard>
  );
}

/** The legend key of one statistic: the open circle of a record, the line of the mean, the swatch
 *  of a stacked segment — the same mark the chart draws it with. */
function StatisticKey({ statistic }: { statistic: Statistic }) {
  if (statistic === 'recordedHigh' || statistic === 'recordedLow') {
    return (
      <svg width={12} height={12} aria-hidden="true">
        <circle cx={6} cy={6} r={MARKER_RADIUS} fill="var(--card-bg)" stroke="var(--chart-ink)" strokeWidth={1.5} />
      </svg>
    );
  }
  if (statistic === 'mean') {
    return (
      <svg width={12} height={12} aria-hidden="true">
        <line x1={0} x2={12} y1={6} y2={6} stroke="var(--chart-ink)" strokeWidth={2} />
      </svg>
    );
  }
  const segment = SEGMENTS.find((candidate) => candidate.name === statistic)!;
  return <i className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: segment.fill }} aria-hidden="true" />;
}
