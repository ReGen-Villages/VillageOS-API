import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { NumberFormat } from '@tendril-ui/tendril';
import type { windowOf } from './historySeries';
import { formatNumber } from './format';

const TOOLTIP_OFFSET = 10;
/** Past this share of the chart's width the tooltip opens to the left of the mark, so it never leaves
 *  the card on the right. */
const FLIP_PAST = 0.6;

/** The window a history chart draws, in words: "last year", "last 10 years", "last 90 days". */
export function HistoryWindow({ window }: { window: ReturnType<typeof windowOf> }) {
  const { t } = useTranslation();
  if (!window) return null;
  return (
    <span className="text-[11px] font-normal normal-case tracking-normal text-zinc-400 dark:text-zinc-500">
      {window.unit === 'years'
        ? t('widgets.history.lastYears', { count: window.count })
        : t('widgets.history.lastDays', { count: window.count })}
    </span>
  );
}

/** The readout for the mark under the pointer or the focus, beside it. Every value it shows is also
 *  in the chart's table twin, so it enhances the chart and gates nothing. */
export function ChartTooltip({
  left,
  top,
  width,
  title,
  children,
}: {
  left: number;
  top: number;
  width: number;
  title: string;
  children: ReactNode;
}) {
  const flipped = left > width * FLIP_PAST;
  return (
    <div
      role="tooltip"
      className="pointer-events-none absolute z-10 rounded-md border border-zinc-200 bg-white px-2.5 py-2 text-xs text-zinc-900 shadow-md dark:border-zinc-700 dark:bg-zinc-800 dark:text-zinc-100"
      style={flipped
        ? { right: width - left + TOOLTIP_OFFSET, top }
        : { left: left + TOOLTIP_OFFSET, top }}
    >
      <div className="mb-1 font-semibold">{title}</div>
      <div className="space-y-0.5">{children}</div>
    </div>
  );
}

/** Every value the chart draws, as a table a screen reader walks and a search finds, which its button
 *  shows to every reader. Visually hidden until then, never absent: a chart whose values can only be
 *  seen is a chart some readers cannot read. `hiddenUntilShown` leaves a table of thousands of cells
 *  out of the page until it is asked for. */
export function SeriesTable({
  caption,
  columns,
  rows,
  hiddenUntilShown = false,
}: {
  caption: string;
  columns: string[];
  rows: { name: string; cells: string[] }[];
  hiddenUntilShown?: boolean;
}) {
  const { t } = useTranslation();
  const [shown, setShown] = useState(false);
  // The hiding class sits on a block rather than on the table: a table never lays out narrower than its
  // cells, so a one-pixel table is still as wide as its values and widens the page on a phone.
  return (
    <>
      <div className="mt-2 flex justify-end">
        <button
          type="button"
          aria-pressed={shown}
          onClick={() => setShown((was) => !was)}
          className="text-[11px] text-zinc-500 hover:text-zinc-800 dark:text-zinc-400 dark:hover:text-zinc-200"
        >
          {t('widgets.table.toggle')}
        </button>
      </div>
      {(shown || !hiddenUntilShown) && (
        <div className={shown ? 'mt-2 max-h-80 overflow-auto text-[11px] text-zinc-700 dark:text-zinc-300' : 'sr-only'}>
          <table className="w-full tabular-nums">
            <caption className={shown ? 'sr-only' : undefined}>{caption}</caption>
            <thead>
              <tr>
                <th scope="col" />
                {columns.map((column) => <th key={column} scope="col" className="px-1.5 py-1 text-right font-medium">{column}</th>)}
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.name} className="border-t border-zinc-200 dark:border-zinc-700">
                  <th scope="row" className="px-1.5 py-1 text-left font-medium">{row.name}</th>
                  {row.cells.map((cell, at) => <td key={at} className="px-1.5 py-1 text-right">{cell}</td>)}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}

export function ValueGridlines({
  ticks,
  y,
  left,
  right,
  format = 'integer',
}: {
  ticks: number[];
  y: (value: number) => number;
  left: number;
  right: number;
  format?: NumberFormat;
}) {
  return ticks.map((tick) => (
    <g key={tick}>
      <line x1={left} x2={right} y1={y(tick)} y2={y(tick)} stroke="var(--chart-grid)" strokeWidth={1} />
      <text x={left - 6} y={y(tick) + 3.5} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)" className="tabular-nums">
        {formatNumber(tick, format)}
      </text>
    </g>
  ));
}

export function MonthColumn({
  monthKey,
  centre,
  markWidth,
  plotTop,
  plotBottom,
  name,
  readOut,
  active,
  onActive,
  children,
}: {
  monthKey: string;
  centre: number;
  markWidth: number;
  plotTop: number;
  plotBottom: number;
  name: string;
  readOut: string;
  active: boolean;
  onActive: (active: boolean) => void;
  children: ReactNode;
}) {
  const left = centre - markWidth / 2;
  const plotHeight = plotBottom - plotTop;
  return (
    <g
      data-month={monthKey}
      role="img"
      tabIndex={0}
      aria-label={readOut}
      onFocus={() => onActive(true)}
      onBlur={() => onActive(false)}
      onMouseEnter={() => onActive(true)}
      onMouseLeave={() => onActive(false)}
      className="outline-none"
    >
      <rect x={left - 6} y={plotTop} width={markWidth + 12} height={plotHeight} fill="transparent" />
      {children}
      <text x={centre} y={plotBottom + 15} fontSize={10} textAnchor="middle" fill="var(--chart-ink-muted)">
        {name}
      </text>
      {active && (
        <rect x={left - 3} y={plotTop} width={markWidth + 6} height={plotHeight} fill="none" stroke="var(--chart-ink-muted)" strokeWidth={1} rx={3} />
      )}
    </g>
  );
}

export function AxisUnit({ unit, left, plotBottom }: { unit: string | undefined; left: number; plotBottom: number }) {
  if (!unit) return null;
  return (
    <text x={left - 6} y={plotBottom + 15} fontSize={10} textAnchor="end" fill="var(--chart-ink-muted)">
      {unit}
    </text>
  );
}

/** The bands a chart draws behind its marks, each between the bounds the model states; a bound left
 *  unbound runs to the edge of the domain. */
export function ChartBands({
  bands,
  domain,
  y,
  left,
  right,
}: {
  bands: { label: string; colour: string; from: number | null; to: number | null }[];
  domain: [number, number];
  y: (value: number) => number;
  left: number;
  right: number;
}) {
  return bands.map((band, at) => {
    const top = y(band.to ?? domain[1]);
    const bottom = y(band.from ?? domain[0]);
    // Labels alternate ends, so two bands that overlap do not write over each other's name.
    const atTheRight = at % 2 === 1;
    return (
      <g key={band.label}>
        <rect data-band={band.label} x={left} y={top} width={right - left} height={Math.max(0, bottom - top)} fill={band.colour} opacity={0.3} />
        <text x={atTheRight ? right - 4 : left + 4} y={top + 11} fontSize={10} textAnchor={atTheRight ? 'end' : 'start'} fill="var(--chart-ink-muted)">
          {band.label}
        </text>
      </g>
    );
  });
}
