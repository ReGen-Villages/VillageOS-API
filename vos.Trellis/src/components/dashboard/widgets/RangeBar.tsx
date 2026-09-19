import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { RangeBarWidget, RangeSeries } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { asNumber } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, HistoryWindow, SeriesTable } from './chartChrome';
import { linearScale, niceTicks, paddedDomain } from './chartScale';
import { groupsByKey, monthNames, windowOf } from './historySeries';
import { formatNumber } from './format';

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

const PLOT_HEIGHT = 220;
const TOP_GUTTER = 8;
const AXIS_BAND = 22;
const LEFT_GUTTER = 40;
const RIGHT_GUTTER = 8;
const FALLBACK_WIDTH = 480;
const WIDEST_BAR = 24;
const TICK_COUNT = 5;
/** The annual bar stands apart from the months with room for its label, as a panel of its own would. */
const ANNUAL_SLOTS = 1.6;
/** A thin ring in the surface colour is the gap between stacked segments and around the circles. */
const SURFACE_GAP = 2;
const MARKER_RADIUS = 4;

type Statistics = Partial<Record<Statistic, number>>;

/** One period's seven statistics, or whichever of them the platform answered. */
function statisticsOf(series: Map<string, number>[], key: string | null): Statistics {
  const period: Statistics = {};
  STATISTICS.forEach((statistic, at) => {
    const groups = series[at];
    const value = key === null ? groups.values().next().value : groups.get(key);
    if (value !== undefined) period[statistic] = value;
  });
  return period;
}

export function RangeBar({ widget, ctx }: { widget: RangeBarWidget; ctx: ResolveContext }) {
  const { t, i18n } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<number | null>(null);

  const monthBindings = STATISTICS.map((statistic) => widget.months[statistic]);
  const annualBindings = widget.annual ? STATISTICS.map((statistic) => widget.annual![statistic]) : [];
  const bandBindings = (widget.bands ?? []).flatMap((band) => [band.from, band.to]);
  const resolved = useBindings([...monthBindings, ...annualBindings, ...bandBindings], ctx);
  const monthSeries = resolved.slice(0, STATISTICS.length).map((state) => groupsByKey(state.value));
  const annualSeries = resolved.slice(STATISTICS.length, STATISTICS.length + annualBindings.length)
    .map((state) => groupsByKey(state.value));
  const bandBounds = resolved.slice(STATISTICS.length + annualBindings.length).map((state) => asNumber(state.value));

  const names = monthNames(i18n.language);
  const periods = [
    ...names.map((name, month) => ({ name, statistics: statisticsOf(monthSeries, String(month + 1)) })),
    ...(widget.annual ? [{ name: t('widgets.rangeBar.annual'), statistics: statisticsOf(annualSeries, null) }] : []),
  ]
    .map((period, at) => ({ ...period, at, annual: at === 12 }))
    .filter((period) => Object.keys(period.statistics).length > 0);

  const bands = (widget.bands ?? []).map((band, at) => ({
    label: band.label,
    colour: band.colour,
    from: bandBounds[at * 2],
    to: bandBounds[at * 2 + 1],
  }));

  const drawn = [
    ...periods.flatMap((period) => Object.values(period.statistics)),
    ...bands.flatMap((band) => [band.from, band.to]).filter((bound): bound is number => bound !== null),
  ];
  const domain = paddedDomain(
    drawn.length ? [Math.min(...drawn), Math.max(...drawn)] : [0, 1], widget.floor, widget.ceiling);
  const width = measuredWidth || FALLBACK_WIDTH;
  const plotTop = TOP_GUTTER;
  const plotBottom = TOP_GUTTER + PLOT_HEIGHT;
  const y = linearScale(domain, [plotBottom, plotTop]);
  const slots = widget.annual ? 12 + ANNUAL_SLOTS : 12;
  const slotWidth = (width - LEFT_GUTTER - RIGHT_GUTTER) / slots;
  const barWidth = Math.min(WIDEST_BAR, slotWidth * 0.6);
  const centreOf = (at: number) => LEFT_GUTTER + slotWidth * (at === 12 ? 12 + ANNUAL_SLOTS / 2 : at + 0.5);
  const ticks = niceTicks(domain[0], domain[1], TICK_COUNT).filter((tick) => tick >= domain[0] && tick <= domain[1]);
  const figure = (value: number | undefined) =>
    value === undefined ? '—' : `${formatNumber(value, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;
  const label = (statistic: Statistic) => t(`widgets.rangeBar.${statistic}`);
  const readOut = (period: (typeof periods)[number]) =>
    `${period.name}: ${STATISTICS.filter((statistic) => period.statistics[statistic] !== undefined)
      .map((statistic) => `${label(statistic)} ${figure(period.statistics[statistic])}`).join(', ')}`;
  const shown = active === null ? undefined : periods.find((period) => period.at === active);

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.months.mean)} />}>
      <div ref={measure} className="relative mt-2">
        <svg
          width={width}
          height={plotBottom + AXIS_BAND}
          viewBox={`0 0 ${width} ${plotBottom + AXIS_BAND}`}
          role="img"
          aria-label={widget.title ?? t('widgets.rangeBar.annual')}
          className="block max-w-full"
        >
          {bands.map((band, at) => {
            const top = y(band.to ?? domain[1]);
            const bottom = y(band.from ?? domain[0]);
            // Labels alternate ends, so two bands that overlap do not write over each other's name.
            const atTheRight = at % 2 === 1;
            return (
              <g key={band.label}>
                <rect
                  data-band={band.label}
                  x={LEFT_GUTTER}
                  y={top}
                  width={width - LEFT_GUTTER - RIGHT_GUTTER}
                  height={Math.max(0, bottom - top)}
                  fill={band.colour}
                  opacity={0.3}
                />
                <text
                  x={atTheRight ? width - RIGHT_GUTTER - 4 : LEFT_GUTTER + 4}
                  y={top + 11}
                  fontSize={10}
                  textAnchor={atTheRight ? 'end' : 'start'}
                  fill="var(--chart-ink-muted)"
                >
                  {band.label}
                </text>
              </g>
            );
          })}
          {ticks.map((tick) => (
            <g key={tick}>
              <line x1={LEFT_GUTTER} x2={width - RIGHT_GUTTER} y1={y(tick)} y2={y(tick)} stroke="var(--chart-grid)" strokeWidth={1} />
              <text x={LEFT_GUTTER - 6} y={y(tick) + 3.5} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)" className="tabular-nums">
                {formatNumber(tick, 'integer')}
              </text>
            </g>
          ))}
          {widget.unit && (
            <text x={LEFT_GUTTER - 6} y={plotBottom + 15} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)">
              {widget.unit}
            </text>
          )}
          {widget.annual && (
            <line
              x1={LEFT_GUTTER + slotWidth * 12} x2={LEFT_GUTTER + slotWidth * 12}
              y1={plotTop} y2={plotBottom} stroke="var(--chart-axis)" strokeWidth={1}
            />
          )}
          {periods.map((period) => {
            const centre = centreOf(period.at);
            const left = centre - barWidth / 2;
            const { statistics } = period;
            return (
              <g
                key={period.at}
                data-month={period.annual ? 'annual' : String(period.at + 1)}
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
            {STATISTICS.filter((statistic) => shown.statistics[statistic] !== undefined).map((statistic) => (
              <div key={statistic} className="flex items-baseline gap-2">
                <StatisticKey statistic={statistic} />
                <span className="font-semibold tabular-nums">{figure(shown.statistics[statistic])}</span>
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
          cells: STATISTICS.map((statistic) => figure(period.statistics[statistic])),
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
