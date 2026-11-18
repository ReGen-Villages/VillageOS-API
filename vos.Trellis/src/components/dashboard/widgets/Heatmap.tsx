import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { Binding, HeatmapWidget } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';
import { asNumber } from '@tendril-ui/tendril';
import { useBindings } from '@tendril-ui/tendril';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, HistoryWindow, SeriesTable } from './chartChrome';
import { rampColour } from './chartRamp';
import { linearScale } from './chartScale';
import { useChartSurface } from './useChartSurface';
import { dayOfYearLabel, groupsByKey, monthNames, monthStarts, windowOf } from './historySeries';
import { formatNumber } from './format';
import { sunTimes } from './sunTimes';

const HOURS = 24;
const DAYS = 366;
const MONTHS = 12;
const PLOT_HEIGHT = 240;
const TOP_GUTTER = 8;
const AXIS_BAND = 22;
const LEFT_GUTTER = 44;
const RIGHT_GUTTER = 8;
const FALLBACK_WIDTH = 480;
const HOUR_TICKS = [0, 6, 12, 18, 24];
const SECONDS_PER_HOUR = 3600;
const LEGEND_STEPS = 24;
/** A month's sun is drawn at its fifteenth day. */
const MIDDLE_OF_MONTH = 14;

/** A column is a day of the year or a month, counted from one. */
type Cell = { hour: number; column: number };

/** How the platform's keys lay out across the columns, and what a column is called. */
interface Columns {
  count: number;
  place: (key: string) => Cell;
  dayOfYear: (column: number) => number;
  name: (column: number, months: string[], locale: string) => string;
  sunLabelColumn: number;
}

const BY_DAY: Columns = {
  count: DAYS,
  place: (key) => {
    const [hour, day] = key.split(',').map(Number);
    return { hour, column: day };
  },
  dayOfYear: (column) => column,
  name: (column, _months, locale) => dayOfYearLabel(column, locale),
  sunLabelColumn: 8,
};

const BY_MONTH: Columns = {
  count: MONTHS,
  place: (key) => {
    const [month, hour] = key.split(',').map(Number);
    return { hour, column: month };
  },
  dayOfYear: (column) => monthStarts()[column - 1] + MIDDLE_OF_MONTH,
  name: (column, months) => months[column - 1],
  sunLabelColumn: 1,
};

function columnsOf(binding: Binding): Columns {
  return binding.kind === 'history' && binding.steps.at(-1)?.fold === 'monthOfYear,hourOfDay' ? BY_MONTH : BY_DAY;
}

/** The grid the platform answered, indexed by column then hour; a cell the platform left out is absent. */
function gridOf(groups: Map<string, number>, columns: Columns): (number | undefined)[][] {
  const grid: (number | undefined)[][] = Array.from({ length: columns.count }, () => Array(HOURS).fill(undefined));
  for (const [key, value] of groups) {
    const { hour, column } = columns.place(key);
    if (hour >= 0 && hour < HOURS && column >= 1 && column <= columns.count) grid[column - 1][hour] = value;
  }
  return grid;
}

type DrawnClass = { label: string; colour: string; from: number | null; to: number | null };

function classHolding(classes: DrawnClass[], value: number): DrawnClass | undefined {
  return classes.find((entry) => (entry.from === null || value >= entry.from) && (entry.to === null || value < entry.to));
}

export function Heatmap({ widget, context }: { widget: HeatmapWidget; context: ResolveContext }) {
  const { t, i18n } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const canvas = useRef<HTMLCanvasElement | null>(null);
  const [active, setActive] = useState<Cell | null>(null);
  const surface = useChartSurface();

  const [value, latitude, longitude, offset, ...classBounds] = useBindings(
    [widget.value, widget.sun?.latitude, widget.sun?.longitude, widget.sun?.utcOffsetSeconds,
      ...(widget.classes ?? []).flatMap((entry) => [entry.from, entry.to])],
    context);
  const columns = columnsOf(widget.value);
  const grid = useMemo(() => gridOf(groupsByKey(value.value), columns), [value.value, columns]);
  // Kept by its contents, because the bindings answer a fresh array on every render.
  const classKey = JSON.stringify((widget.classes ?? []).map((entry, at) => ({
    label: entry.label,
    colour: entry.colour,
    from: entry.from ? asNumber(classBounds[at * 2]?.value ?? null) : null,
    to: entry.to ? asNumber(classBounds[at * 2 + 1]?.value ?? null) : null,
  })));
  const classes = useMemo<DrawnClass[]>(() => JSON.parse(classKey), [classKey]);
  // Fixed once per answer rather than per render: the pointer moving over the grid re-renders the
  // tooltip, and must not walk the cells again or repaint them.
  const [floor, ceiling] = useMemo(() => {
    let lowest = Infinity;
    let highest = -Infinity;
    for (const column of grid) for (const cell of column) {
      if (cell === undefined) continue;
      if (cell < lowest) lowest = cell;
      if (cell > highest) highest = cell;
    }
    // A colour scale gets no air: the palest cell is the coldest hour, not a value nothing reached.
    const [lowestDrawn, highestDrawn] = lowest <= highest ? [lowest, highest] : [0, 1];
    return [widget.floor ?? lowestDrawn, widget.ceiling ?? highestDrawn];
  }, [grid, widget.floor, widget.ceiling]);
  const classShares = useMemo(() => {
    const counts = new Map<DrawnClass, number>();
    let answered = 0;
    for (const column of grid) for (const cell of column) {
      if (cell === undefined) continue;
      answered += 1;
      const holding = classHolding(classes, cell);
      if (holding) counts.set(holding, (counts.get(holding) ?? 0) + 1);
    }
    return classes.map((entry) => ({ ...entry, share: answered === 0 ? 0 : (counts.get(entry) ?? 0) / answered }));
  }, [grid, classes]);

  const width = measuredWidth || FALLBACK_WIDTH;
  const plotWidth = width - LEFT_GUTTER - RIGHT_GUTTER;
  const cellWidth = plotWidth / columns.count;
  const cellHeight = PLOT_HEIGHT / HOURS;
  const x = (column: number) => LEFT_GUTTER + (column - 1) * cellWidth;
  const y = (hour: number) => TOP_GUTTER + hour * cellHeight;
  const figure = (cell: number | undefined) =>
    cell === undefined ? '—' : `${formatNumber(cell, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;

  useEffect(() => {
    const element = canvas.current;
    const context = element?.getContext('2d');
    if (!element || !context) return;
    const ratio = window.devicePixelRatio || 1;
    element.width = Math.round(plotWidth * ratio);
    element.height = Math.round(PLOT_HEIGHT * ratio);
    context.setTransform(ratio, 0, 0, ratio, 0, 0);
    context.clearRect(0, 0, plotWidth, PLOT_HEIGHT);
    // The cells overlap their neighbours by a hair so no gap shows between them at any width; the
    // canvas is the one place the two-pixel surface gap is not drawn, because a cell is one pixel wide
    // on a phone.
    const across = cellWidth + 0.5;
    const down = cellHeight + 0.5;
    const share = linearScale([floor, ceiling], [0, 1]);
    grid.forEach((column, columnIndex) => {
      column.forEach((cell, hour) => {
        if (cell === undefined) return;
        const colour = classShares.length > 0 ? classHolding(classShares, cell)?.colour : rampColour(share(cell), surface);
        if (!colour) return;
        context.fillStyle = colour;
        context.fillRect(columnIndex * cellWidth, hour * cellHeight, across, down);
      });
    });
  }, [grid, classShares, floor, ceiling, surface, plotWidth, cellWidth, cellHeight]);

  const sun = useMemo(() => {
    const at = asNumber(latitude.value);
    const along = asNumber(longitude.value);
    if (!widget.sun || at === null || along === null) return null;
    const offsetHours = (asNumber(offset.value) ?? 0) / SECONDS_PER_HOUR;
    return Array.from({ length: columns.count }, (_, index) => sunTimes(columns.dayOfYear(index + 1), at, along, offsetHours));
  }, [widget.sun, latitude.value, longitude.value, offset.value, columns]);
  const curve = (hourOf: (day: { sunrise: number; sunset: number }) => number) =>
    (sun ?? [])
      .map((day, index) => `${index === 0 ? 'M' : 'L'}${(x(index + 1) + cellWidth / 2).toFixed(1)},${y(hourOf(day)).toFixed(1)}`)
      .join(' ');

  const extremes = useMemo(() => {
    let warmest: { cell: Cell; value: number } | null = null;
    let coldest: { cell: Cell; value: number } | null = null;
    grid.forEach((column, columnIndex) => column.forEach((cell, hour) => {
      if (cell === undefined) return;
      if (!warmest || cell > warmest.value) warmest = { cell: { hour, column: columnIndex + 1 }, value: cell };
      if (!coldest || cell < coldest.value) coldest = { cell: { hour, column: columnIndex + 1 }, value: cell };
    }));
    return { warmest, coldest } as { warmest: { cell: Cell; value: number } | null; coldest: { cell: Cell; value: number } | null };
  }, [grid]);

  const names = monthNames(i18n.language);
  const whenIs = (cell: Cell) => `${columns.name(cell.column, names, i18n.language)}, ${String(cell.hour).padStart(2, '0')}:00`;
  const description = [
    widget.title ?? '',
    extremes.warmest ? t('widgets.heatmap.warmest', { value: figure(extremes.warmest.value), when: whenIs(extremes.warmest.cell) }) : '',
    extremes.coldest ? t('widgets.heatmap.coldest', { value: figure(extremes.coldest.value), when: whenIs(extremes.coldest.cell) }) : '',
  ].filter((part) => part !== '').join('. ');

  const cellUnder = (event: MouseEvent<HTMLCanvasElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    const column = Math.min(columns.count, Math.max(1, Math.floor(((event.clientX - box.left) / box.width) * columns.count) + 1));
    const hour = Math.min(HOURS - 1, Math.max(0, Math.floor(((event.clientY - box.top) / box.height) * HOURS)));
    setActive({ hour, column });
  };
  const step = (event: KeyboardEvent<HTMLCanvasElement>) => {
    const moves: Record<string, [number, number]> = {
      ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1],
    };
    const move = moves[event.key];
    if (!move) return;
    event.preventDefault();
    setActive((current) => {
      const from = current ?? { hour: 12, column: 1 };
      return {
        column: Math.min(columns.count, Math.max(1, from.column + move[0])),
        hour: Math.min(HOURS - 1, Math.max(0, from.hour + move[1])),
      };
    });
  };

  const legend = Array.from({ length: LEGEND_STEPS }, (_, at) => rampColour(at / (LEGEND_STEPS - 1), surface));

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={<HistoryWindow window={windowOf(widget.value)} />}>
      <div ref={measure} className="relative mt-2">
        <div role="img" aria-label={description} className="relative" style={{ width, height: TOP_GUTTER + PLOT_HEIGHT + AXIS_BAND }}>
          <canvas
            ref={canvas}
            tabIndex={0}
            aria-hidden="true"
            onMouseMove={cellUnder}
            onMouseLeave={() => setActive(null)}
            onFocus={() => setActive((current) => current ?? { hour: 12, column: 1 })}
            onBlur={() => setActive(null)}
            onKeyDown={step}
            className="absolute outline-none focus-visible:ring-2 focus-visible:ring-emerald-500"
            style={{ left: LEFT_GUTTER, top: TOP_GUTTER, width: plotWidth, height: PLOT_HEIGHT }}
          />
          <svg
            width={width}
            height={TOP_GUTTER + PLOT_HEIGHT + AXIS_BAND}
            viewBox={`0 0 ${width} ${TOP_GUTTER + PLOT_HEIGHT + AXIS_BAND}`}
            aria-hidden="true"
            className="pointer-events-none absolute inset-0 block max-w-full"
          >
            {HOUR_TICKS.map((hour) => (
              <text key={hour} x={LEFT_GUTTER - 6} y={y(hour) + 3.5} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)" className="tabular-nums">
                {`${String(hour).padStart(2, '0')}:00`}
              </text>
            ))}
            {columns === BY_DAY
              ? monthStarts().map((start, month) => (
                <g key={start}>
                  {month > 0 && (
                    <line x1={x(start)} x2={x(start)} y1={TOP_GUTTER} y2={TOP_GUTTER + PLOT_HEIGHT} stroke="var(--card-bg)" strokeWidth={1} opacity={0.6} />
                  )}
                  <text x={x(start) + (cellWidth * 30) / 2} y={TOP_GUTTER + PLOT_HEIGHT + 15} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)">
                    {names[month]}
                  </text>
                </g>
              ))
              : names.map((name, month) => (
                <text key={name} x={x(month + 1) + cellWidth / 2} y={TOP_GUTTER + PLOT_HEIGHT + 15} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)">
                  {name}
                </text>
              ))}
            {sun && (
              <>
                <path data-curve="sunrise" data-first-hour={sun[0].sunrise} d={curve((day) => day.sunrise)} fill="none" stroke="var(--chart-ink)" strokeWidth={3} opacity={0.5} />
                <path d={curve((day) => day.sunrise)} fill="none" stroke="var(--card-bg)" strokeWidth={1.5} />
                <path data-curve="sunset" data-first-hour={sun[0].sunset} d={curve((day) => day.sunset)} fill="none" stroke="var(--chart-ink)" strokeWidth={3} opacity={0.5} />
                <path d={curve((day) => day.sunset)} fill="none" stroke="var(--card-bg)" strokeWidth={1.5} />
                <text x={x(columns.sunLabelColumn)} y={y(sun[columns.sunLabelColumn - 1].sunrise) - 5} fontSize={10} fill="var(--chart-ink)">{t('widgets.heatmap.sunrise')}</text>
                <text x={x(columns.sunLabelColumn)} y={y(sun[columns.sunLabelColumn - 1].sunset) + 13} fontSize={10} fill="var(--chart-ink)">{t('widgets.heatmap.sunset')}</text>
              </>
            )}
            {active && (
              <rect x={x(active.column)} y={y(active.hour)} width={Math.max(cellWidth, 2)} height={cellHeight} fill="none" stroke="var(--chart-ink)" strokeWidth={1.5} />
            )}
          </svg>
        </div>
        {active && (
          <ChartTooltip left={x(active.column)} top={y(active.hour)} width={width} title={whenIs(active)}>
            <span className="font-semibold tabular-nums">{figure(grid[active.column - 1][active.hour])}</span>
          </ChartTooltip>
        )}
      </div>
      {classShares.length > 0 ? (
        <ul className="mt-2 flex flex-wrap gap-x-3 gap-y-1 text-[11px] text-zinc-500 dark:text-zinc-400">
          {classShares.map((entry) => (
            <li key={entry.label} className="inline-flex items-center gap-1.5">
              <i className="inline-block h-2.5 w-2.5 rounded-sm" style={{ background: entry.colour }} aria-hidden="true" />
              {`${entry.label} ${formatNumber(entry.share, 'percent')}`}
            </li>
          ))}
        </ul>
      ) : (
        <div className="mt-2 flex items-center gap-2 text-[11px] text-zinc-500 dark:text-zinc-400">
          <span className="tabular-nums">{figure(floor)}</span>
          <span className="flex h-2.5 flex-1 max-w-48 overflow-hidden rounded-sm" aria-hidden="true">
            {legend.map((colour, at) => <i key={at} className="flex-1" style={{ background: colour }} />)}
          </span>
          <span className="tabular-nums">{figure(ceiling)}</span>
        </div>
      )}
      <SeriesTable
        caption={widget.title ?? ''}
        columns={Array.from({ length: HOURS }, (_, hour) => `${String(hour).padStart(2, '0')}:00`)}
        rows={grid.map((column, at) => ({ name: columns.name(at + 1, names, i18n.language), cells: column.map(figure) }))}
        hiddenUntilShown={columns === BY_DAY}
      />
    </WidgetCard>
  );
}
