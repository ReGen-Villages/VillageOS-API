/**
 * The pieces every hand-drawn chart shares beside its marks: the window label a history chart carries,
 * the tooltip a hovered or focused mark opens, and the table twin that makes every value reachable
 * without either.
 */
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import type { windowOf } from './historySeries';

/** How far a tooltip stands off the mark it describes. */
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

/** Every value the chart draws, as a table a screen reader walks and a search finds. Visually hidden,
 *  never absent: a chart whose values can only be seen is a chart some readers cannot read. */
export function SeriesTable({
  caption,
  columns,
  rows,
}: {
  caption: string;
  columns: string[];
  rows: { name: string; cells: string[] }[];
}) {
  return (
    <table className="sr-only">
      <caption>{caption}</caption>
      <thead>
        <tr>
          <th scope="col" />
          {columns.map((column) => <th key={column} scope="col">{column}</th>)}
        </tr>
      </thead>
      <tbody>
        {rows.map((row) => (
          <tr key={row.name}>
            <th scope="row">{row.name}</th>
            {row.cells.map((cell, at) => <td key={at}>{cell}</td>)}
          </tr>
        ))}
      </tbody>
    </table>
  );
}
