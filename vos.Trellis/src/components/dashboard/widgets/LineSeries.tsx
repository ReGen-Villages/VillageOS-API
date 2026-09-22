import { useState, type KeyboardEvent, type MouseEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { LineSeriesWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, HistoryWindow, SeriesTable } from './chartChrome';
import { linearScale, niceTicks, paddedDomain } from './chartScale';
import { groupsByKey, windowOf } from './historySeries';
import { formatNumber } from './format';

const PLOT_HEIGHT = 220;
const TOP_GUTTER = 8;
const AXIS_BAND = 22;
const LEFT_GUTTER = 40;
const RIGHT_GUTTER = 8;
const FALLBACK_WIDTH = 480;
const TICK_COUNT = 5;
const LINE_WIDTH = 2;
const MARKER_RADIUS = 2.5;
const SURFACE_GAP = 2;
/** The categorical palette has this many slots; a series past them takes none and is not drawn. */
const PALETTE_SLOTS = 8;

/** Where the calendar axis is labelled: the first group of each year, with the year. A key of any
 *  calendar fold begins with its year, so this reads the same for a day, a month or a year fold. */
function yearTicks(keys: string[]): { at: number; label: string }[] {
  const ticks: { at: number; label: string }[] = [];
  let previous: string | null = null;
  keys.forEach((key, at) => {
    const year = key.slice(0, 4);
    if (year !== previous) ticks.push({ at, label: year });
    previous = year;
  });
  return ticks;
}

export function LineSeries({ widget, context }: { widget: LineSeriesWidget; context: ResolveContext }) {
  const { t } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const [active, setActive] = useState<number | null>(null);

  const resolved = useBindings(widget.series.map((entry) => entry.value), context);
  const series = widget.series
    .map((entry, at) => ({ label: entry.label, slot: at + 1, groups: groupsByKey(resolved[at]?.value ?? null) }))
    .filter((entry) => entry.groups.size > 0 && entry.slot <= PALETTE_SLOTS);
  const keys = [...new Set(series.flatMap((entry) => [...entry.groups.keys()]))].sort();

  const drawn = series.flatMap((entry) => [...entry.groups.values()]);
  const domain = paddedDomain(
    drawn.length ? [Math.min(...drawn), Math.max(...drawn)] : [0, 1], widget.floor, widget.ceiling);
  const width = measuredWidth || FALLBACK_WIDTH;
  const plotTop = TOP_GUTTER;
  const plotBottom = TOP_GUTTER + PLOT_HEIGHT;
  const y = linearScale(domain, [plotBottom, plotTop]);
  const x = linearScale([0, Math.max(1, keys.length - 1)], [LEFT_GUTTER, width - RIGHT_GUTTER]);
  const ticks = niceTicks(domain[0], domain[1], TICK_COUNT).filter((tick) => tick >= domain[0] && tick <= domain[1]);
  const figure = (value: number | undefined) =>
    value === undefined ? '—' : `${formatNumber(value, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;

  const nearest = (event: MouseEvent<SVGSVGElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    const across = ((event.clientX - box.left) / box.width) * width;
    const at = Math.round(((across - LEFT_GUTTER) / (width - LEFT_GUTTER - RIGHT_GUTTER)) * (keys.length - 1));
    setActive(Math.min(keys.length - 1, Math.max(0, at)));
  };
  const step = (event: KeyboardEvent<SVGSVGElement>) => {
    if (keys.length === 0) return;
    const moves: Record<string, number> = { ArrowLeft: -1, ArrowRight: 1, Home: -Infinity, End: Infinity };
    const move = moves[event.key];
    if (move === undefined) return;
    event.preventDefault();
    setActive((current) => Math.min(keys.length - 1, Math.max(0, (current ?? keys.length - 1) + move)));
  };

  const shown = active === null ? undefined : keys[active];

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.series[0]?.value)} />}>
      <div ref={measure} className="relative mt-2">
        <svg
          width={width}
          height={plotBottom + AXIS_BAND}
          viewBox={`0 0 ${width} ${plotBottom + AXIS_BAND}`}
          role="img"
          aria-label={`${widget.title ?? ''}: ${series.map((entry) => entry.label).join(', ')}`}
          tabIndex={0}
          onMouseMove={nearest}
          onMouseLeave={() => setActive(null)}
          onFocus={() => setActive(keys.length - 1)}
          onBlur={() => setActive(null)}
          onKeyDown={step}
          className="block max-w-full outline-none focus-visible:ring-2 focus-visible:ring-emerald-500"
        >
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
          {yearTicks(keys).map((tick) => (
            <g key={tick.label}>
              <line x1={x(tick.at)} x2={x(tick.at)} y1={plotTop} y2={plotBottom} stroke="var(--chart-grid)" strokeWidth={1} />
              <text x={x(tick.at)} y={plotBottom + 15} fontSize={10} textAnchor="start" fill="var(--chart-ink-muted)">
                {tick.label}
              </text>
            </g>
          ))}
          {series.map((entry) => {
            const stroke = `var(--series-${entry.slot})`;
            const points = keys
              .map((key, at) => ({ at, value: entry.groups.get(key) }))
              .filter((point): point is { at: number; value: number } => point.value !== undefined);
            const path = points
              .map((point, index) => `${index === 0 ? 'M' : 'L'}${x(point.at).toFixed(1)},${y(point.value).toFixed(1)}`)
              .join(' ');
            // The markers go under the line: a ring on a marker beside the next one would cut the line
            // between them, and a month is only a few pixels from its neighbour.
            return (
              <g key={entry.label}>
                {points.map((point) => (
                  <circle
                    key={point.at}
                    data-series={entry.label}
                    cx={x(point.at)}
                    cy={y(point.value)}
                    r={MARKER_RADIUS}
                    fill={stroke}
                    stroke="var(--card-bg)"
                    strokeWidth={SURFACE_GAP}
                  />
                ))}
                <path data-series={entry.label} d={path} fill="none" stroke={stroke} strokeWidth={LINE_WIDTH} strokeLinejoin="round" strokeLinecap="round" />
              </g>
            );
          })}
          {active !== null && keys.length > 0 && (
            <line x1={x(active)} x2={x(active)} y1={plotTop} y2={plotBottom} stroke="var(--chart-ink-muted)" strokeWidth={1} />
          )}
        </svg>
        {shown !== undefined && active !== null && (
          <ChartTooltip left={x(active)} top={plotTop} width={width} title={shown}>
            {series.map((entry) => (
              <div key={entry.label} className="flex items-baseline gap-2">
                <i className="inline-block h-0.5 w-3" style={{ background: `var(--series-${entry.slot})` }} aria-hidden="true" />
                <span className="font-semibold tabular-nums">{figure(entry.groups.get(shown))}</span>
                <span className="text-zinc-500 dark:text-zinc-400">{entry.label}</span>
              </div>
            ))}
          </ChartTooltip>
        )}
      </div>
      {series.length > 0 && (
        <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
          {series.map((entry) => (
            <li key={entry.label} className="inline-flex items-center gap-1.5">
              <i className="inline-block h-0.5 w-3" style={{ background: `var(--series-${entry.slot})` }} aria-hidden="true" />
              {entry.label}
            </li>
          ))}
        </ul>
      )}
      <SeriesTable
        caption={widget.title ?? t('widgets.history.series')}
        columns={series.map((entry) => entry.label)}
        rows={keys.map((key) => ({ name: key, cells: series.map((entry) => figure(entry.groups.get(key))) }))}
      />
    </WidgetCard>
  );
}
