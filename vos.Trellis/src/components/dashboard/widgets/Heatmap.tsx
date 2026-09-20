import { useEffect, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent } from 'react';
import { useTranslation } from 'react-i18next';
import type { HeatmapWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';
import { asNumber } from '../../../api/dashboardApi';
import { useBindings } from '../../../hooks/useDashboard';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { WidgetCard } from './WidgetCard';
import { ChartTooltip, HistoryWindow } from './chartChrome';
import { rampColour, type Surface } from './chartRamp';
import { linearScale } from './chartScale';
import { dayOfYearLabel, groupsByKey, monthNames, monthStarts, windowOf } from './historySeries';
import { formatNumber } from './format';
import { sunTimes } from './sunTimes';

const HOURS = 24;
const DAYS = 366;
const PLOT_HEIGHT = 240;
const TOP_GUTTER = 8;
const AXIS_BAND = 22;
const LEFT_GUTTER = 44;
const RIGHT_GUTTER = 8;
const FALLBACK_WIDTH = 480;
const HOUR_TICKS = [0, 6, 12, 18, 24];
const SECONDS_PER_HOUR = 3600;
const LEGEND_STEPS = 24;

type Cell = { hour: number; day: number };

/** The grid the platform answered, indexed by day then hour; a cell the platform left out is absent. */
function gridOf(groups: Map<string, number>): (number | undefined)[][] {
  const grid: (number | undefined)[][] = Array.from({ length: DAYS }, () => Array(HOURS).fill(undefined));
  for (const [key, value] of groups) {
    const [hour, day] = key.split(',').map(Number);
    if (hour >= 0 && hour < HOURS && day >= 1 && day <= DAYS) grid[day - 1][hour] = value;
  }
  return grid;
}

function surfaceOf(): Surface {
  return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
}

export function Heatmap({ widget, context }: { widget: HeatmapWidget; context: ResolveContext }) {
  const { t, i18n } = useTranslation();
  const [measure, measuredWidth] = useElementWidth();
  const canvas = useRef<HTMLCanvasElement | null>(null);
  const [active, setActive] = useState<Cell | null>(null);
  const [surface, setSurface] = useState<Surface>(surfaceOf);

  const [value, latitude, longitude, offset] = useBindings(
    [widget.value, widget.sun?.latitude, widget.sun?.longitude, widget.sun?.utcOffsetSeconds], context);
  const grid = useMemo(() => gridOf(groupsByKey(value.value)), [value.value]);
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

  const width = measuredWidth || FALLBACK_WIDTH;
  const plotWidth = width - LEFT_GUTTER - RIGHT_GUTTER;
  const cellWidth = plotWidth / DAYS;
  const cellHeight = PLOT_HEIGHT / HOURS;
  const x = (day: number) => LEFT_GUTTER + (day - 1) * cellWidth;
  const y = (hour: number) => TOP_GUTTER + hour * cellHeight;
  const figure = (cell: number | undefined) =>
    cell === undefined ? '—' : `${formatNumber(cell, widget.format)}${widget.unit ? ` ${widget.unit}` : ''}`;

  // The theme is a class on the document, and the canvas cannot read a token the way an SVG can, so
  // the surface is watched and the grid repainted when it changes.
  useEffect(() => {
    const observer = new MutationObserver(() => setSurface(surfaceOf()));
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['class'] });
    return () => observer.disconnect();
  }, []);

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
    grid.forEach((column, dayIndex) => {
      column.forEach((cell, hour) => {
        if (cell === undefined) return;
        context.fillStyle = rampColour(share(cell), surface);
        context.fillRect(dayIndex * cellWidth, hour * cellHeight, across, down);
      });
    });
  }, [grid, floor, ceiling, surface, plotWidth, cellWidth, cellHeight]);

  const sun = useMemo(() => {
    const at = asNumber(latitude.value);
    const along = asNumber(longitude.value);
    if (!widget.sun || at === null || along === null) return null;
    const offsetHours = (asNumber(offset.value) ?? 0) / SECONDS_PER_HOUR;
    return Array.from({ length: DAYS }, (_, index) => sunTimes(index + 1, at, along, offsetHours));
  }, [widget.sun, latitude.value, longitude.value, offset.value]);
  const curve = (hourOf: (day: { sunrise: number; sunset: number }) => number) =>
    (sun ?? [])
      .map((day, index) => `${index === 0 ? 'M' : 'L'}${(x(index + 1) + cellWidth / 2).toFixed(1)},${y(hourOf(day)).toFixed(1)}`)
      .join(' ');

  const extremes = useMemo(() => {
    let warmest: { cell: Cell; value: number } | null = null;
    let coldest: { cell: Cell; value: number } | null = null;
    grid.forEach((column, dayIndex) => column.forEach((cell, hour) => {
      if (cell === undefined) return;
      if (!warmest || cell > warmest.value) warmest = { cell: { hour, day: dayIndex + 1 }, value: cell };
      if (!coldest || cell < coldest.value) coldest = { cell: { hour, day: dayIndex + 1 }, value: cell };
    }));
    return { warmest, coldest } as { warmest: { cell: Cell; value: number } | null; coldest: { cell: Cell; value: number } | null };
  }, [grid]);

  const whenIs = (cell: Cell) => `${dayOfYearLabel(cell.day, i18n.language)}, ${String(cell.hour).padStart(2, '0')}:00`;
  const description = [
    widget.title ?? '',
    extremes.warmest ? t('widgets.heatmap.warmest', { value: figure(extremes.warmest.value), when: whenIs(extremes.warmest.cell) }) : '',
    extremes.coldest ? t('widgets.heatmap.coldest', { value: figure(extremes.coldest.value), when: whenIs(extremes.coldest.cell) }) : '',
  ].filter((part) => part !== '').join('. ');

  const cellUnder = (event: MouseEvent<HTMLCanvasElement>) => {
    const box = event.currentTarget.getBoundingClientRect();
    const day = Math.min(DAYS, Math.max(1, Math.floor(((event.clientX - box.left) / box.width) * DAYS) + 1));
    const hour = Math.min(HOURS - 1, Math.max(0, Math.floor(((event.clientY - box.top) / box.height) * HOURS)));
    setActive({ hour, day });
  };
  const step = (event: KeyboardEvent<HTMLCanvasElement>) => {
    const moves: Record<string, [number, number]> = {
      ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1],
    };
    const move = moves[event.key];
    if (!move) return;
    event.preventDefault();
    setActive((current) => {
      const from = current ?? { hour: 12, day: 1 };
      return {
        day: Math.min(DAYS, Math.max(1, from.day + move[0])),
        hour: Math.min(HOURS - 1, Math.max(0, from.hour + move[1])),
      };
    });
  };

  const names = monthNames(i18n.language);
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
            onFocus={() => setActive((current) => current ?? { hour: 12, day: 1 })}
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
            {monthStarts().map((start, month) => (
              <g key={start}>
                {month > 0 && (
                  <line x1={x(start)} x2={x(start)} y1={TOP_GUTTER} y2={TOP_GUTTER + PLOT_HEIGHT} stroke="var(--card-bg)" strokeWidth={1} opacity={0.6} />
                )}
                <text x={x(start) + (cellWidth * 30) / 2} y={TOP_GUTTER + PLOT_HEIGHT + 15} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)">
                  {names[month]}
                </text>
              </g>
            ))}
            {sun && (
              <>
                <path data-curve="sunrise" data-first-hour={sun[0].sunrise} d={curve((day) => day.sunrise)} fill="none" stroke="var(--chart-ink)" strokeWidth={3} opacity={0.5} />
                <path d={curve((day) => day.sunrise)} fill="none" stroke="var(--card-bg)" strokeWidth={1.5} />
                <path data-curve="sunset" data-first-hour={sun[0].sunset} d={curve((day) => day.sunset)} fill="none" stroke="var(--chart-ink)" strokeWidth={3} opacity={0.5} />
                <path d={curve((day) => day.sunset)} fill="none" stroke="var(--card-bg)" strokeWidth={1.5} />
                <text x={x(8)} y={y(sun[7].sunrise) - 5} fontSize={10} fill="var(--chart-ink)">{t('widgets.heatmap.sunrise')}</text>
                <text x={x(8)} y={y(sun[7].sunset) + 13} fontSize={10} fill="var(--chart-ink)">{t('widgets.heatmap.sunset')}</text>
              </>
            )}
            {active && (
              <rect x={x(active.day)} y={y(active.hour)} width={Math.max(cellWidth, 2)} height={cellHeight} fill="none" stroke="var(--chart-ink)" strokeWidth={1.5} />
            )}
          </svg>
        </div>
        {active && (
          <ChartTooltip left={x(active.day)} top={y(active.hour)} width={width} title={whenIs(active)}>
            <span className="font-semibold tabular-nums">{figure(grid[active.day - 1][active.hour])}</span>
          </ChartTooltip>
        )}
      </div>
      <div className="mt-2 flex items-center gap-2 text-[11px] text-zinc-500 dark:text-zinc-400">
        <span className="tabular-nums">{figure(floor)}</span>
        <span className="flex h-2.5 flex-1 max-w-48 overflow-hidden rounded-sm" aria-hidden="true">
          {legend.map((colour, at) => <i key={at} className="flex-1" style={{ background: colour }} />)}
        </span>
        <span className="tabular-nums">{figure(ceiling)}</span>
      </div>
    </WidgetCard>
  );
}
