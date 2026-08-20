import { useMemo, useState, type UIEvent } from 'react';
import type { Binding, TableColumn } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';
import { asRows, filterRows } from '../../../api/dashboardApi';
import { useBinding } from '../../../hooks/useDashboard';
import { useElementHeight } from '../../../hooks/useElementHeight';
import { formatNumber, badgeTone, columnMaxima } from './format';

/* One body row of the visibleRows cap: the 1.5 line box at the table font, plus the py-2 padding
   and bottom border of the cells below. In em, so the cap follows the font size — which is why
   `text-[12.5px]` sits on the scroll container the cap applies to rather than on the table. The
   header is measured rather than derived: its labels wrap in narrow columns, and column widths
   depend on the data, so no constant is right for every table. */
const BODY_ROW_HEIGHT = '(1.5em + 1rem + 1px)';

/* The same box in pixels at the `text-[12.5px]` the scroll container sets. The row window assumes
   it until a rendered row reports its own height, and keeps assuming it where no ResizeObserver
   reports one — without an assumption the first paint of a long list would put every row in the
   document, which is the cost this exists to avoid. The spacers assume the same number, so they
   place the window exactly; only the rows actually rendered can drift, which is why being a few
   pixels out stays inside the overscan however far the list is scrolled. */
const ESTIMATED_BODY_ROW_HEIGHT = 12.5 * 1.5 + 16 + 1;

/* Rows kept in the document above and below the cap, so a small scroll reveals a row that is
   already there rather than a gap waiting for the next render. */
const OVERSCAN_ROWS = 6;

/** Sortable, generic data table driven by a rows binding + column spec.
 *  Rows can come from a `rowsBinding` (resolved here) or be passed in directly
 *  via `rows` (e.g. the funnel's cross-stage search results). */
export function DataTable({
  columns,
  rowsBinding,
  rows: rowsProp,
  ctx,
  minWidth = 520,
  sortKey,
  sortDir = 'desc',
  visibleRows,
  emptyLabel = 'No rows.',
  footnote,
  query,
  searchKeys,
  onRowClick,
}: {
  columns: TableColumn[];
  rowsBinding?: Binding;
  rows?: Row[];
  ctx: ResolveContext;
  minWidth?: number;
  sortKey?: string;
  sortDir?: 'asc' | 'desc';
  visibleRows?: number;
  emptyLabel?: string;
  footnote?: string;
  query?: string;
  searchKeys?: string[];
  onRowClick?: (row: Row) => void;
}) {
  const { loading, value } = useBinding(rowsBinding, ctx);
  const [headerRef, headerHeight] = useElementHeight();
  /* Measured from whichever row is at the top of the window, and only until a height comes back:
     the row at the top is a different element after every scroll that moves the window, so keeping
     it observed would tear down and rebuild an observer once per row crossed. Rows are one line of
     a fixed size, so one measurement holds for all of them. */
  const [bodyRowRef, measuredRowHeight] = useElementHeight();
  const [firstVisibleRow, setFirstVisibleRow] = useState(0);
  const resolved = rowsProp ?? asRows(value);
  const rows = useMemo(() => filterRows(resolved, query, searchKeys), [resolved, query, searchKeys]);
  const [sort, setSort] = useState<{ key: string; dir: 1 | -1 }>({
    key: sortKey ?? columns[0]?.key ?? '',
    dir: sortDir === 'asc' ? 1 : -1,
  });

  const sorted = useMemo(() => {
    const col = columns.find((c) => c.key === sort.key);
    const copy = [...rows];
    copy.sort((a, b) => {
      let x = a[sort.key];
      let y = b[sort.key];
      if (col?.numeric) {
        x = Number(x) || 0;
        y = Number(y) || 0;
        return ((x as number) - (y as number)) * sort.dir;
      }
      const sx = String(x ?? '').toLowerCase();
      const sy = String(y ?? '').toLowerCase();
      return (sx < sy ? -1 : sx > sy ? 1 : 0) * sort.dir;
    });
    return copy;
  }, [rows, sort, columns]);

  const maxByKey = useMemo(() => columnMaxima(rows, columns), [rows, columns]);

  /* Only a capped table owns a scroll container of a known height, so only a capped table can say
     which rows are in view. Sorting and searching stay over the whole list: what a row cap bounds is
     the document, never the rows a reader can reach. */
  const rowHeight = measuredRowHeight || ESTIMATED_BODY_ROW_HEIGHT;
  const windowSize = visibleRows ? visibleRows + 2 * OVERSCAN_ROWS : 0;
  const windowed = windowSize > 0 && sorted.length > windowSize;
  const firstShown = windowed
    ? Math.min(Math.max(0, firstVisibleRow - OVERSCAN_ROWS), sorted.length - windowSize)
    : 0;
  const shown = windowed ? sorted.slice(firstShown, firstShown + windowSize) : sorted;
  const rowsBelow = sorted.length - firstShown - shown.length;

  /* The position is kept as a row index, not as the pixel offset it is read from: a scroll within
     one row leaves it unchanged and costs no render, so scrolling re-renders once per row crossed
     rather than once per frame.

     Followed whenever a capped table scrolls, not only while it is windowed — a search that narrows
     the list inside the cap sends the container back to the top, and the remembered position has to
     come back with it, or clearing the search would show rows the scrollbar disagrees with. */
  const followScroll = visibleRows
    ? (event: UIEvent<HTMLDivElement>) => setFirstVisibleRow(Math.floor(event.currentTarget.scrollTop / rowHeight))
    : undefined;

  function toggleSort(key: string, numeric?: boolean) {
    setSort((s) => (s.key === key ? { key, dir: (s.dir * -1) as 1 | -1 } : { key, dir: numeric ? -1 : 1 }));
  }

  if (loading) return <div className="py-6 text-center text-xs text-zinc-400">Loading…</div>;
  if (!rows.length)
    return (
      <div className="py-6 text-center text-xs text-zinc-400">
        {query?.trim() ? `No matches for “${query.trim()}”.` : emptyLabel}
      </div>
    );

  return (
    <div>
      <div
        className={`overflow-x-auto text-[12.5px] ${visibleRows ? 'overflow-y-auto' : ''}`}
        style={visibleRows ? { maxHeight: `calc(${headerHeight}px + ${visibleRows} * ${BODY_ROW_HEIGHT})` } : undefined}
        onScroll={followScroll}
      >
        <table className="w-full border-collapse" style={{ minWidth }}>
          <thead>
            <tr ref={visibleRows ? headerRef : undefined}>
              {columns.map((c) => (
                <th
                  key={c.key}
                  onClick={() => toggleSort(c.key, c.numeric)}
                  className={`sticky top-0 bg-white dark:bg-zinc-800 px-2.5 py-2 border-b border-zinc-200 dark:border-zinc-700 text-[10.5px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400 font-semibold cursor-pointer select-none hover:text-zinc-800 dark:hover:text-zinc-200 ${
                    c.numeric ? 'text-right' : 'text-left'
                  }`}
                >
                  {c.label}
                  {sort.key === c.key && <span className="opacity-50 text-[9px] ml-1">{sort.dir > 0 ? '▲' : '▼'}</span>}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            <SpacerRow height={firstShown * rowHeight} columnCount={columns.length} />
            {shown.map((r, i) => (
              <tr
                key={(r.id as string) ?? i}
                ref={windowed && i === 0 && !measuredRowHeight ? bodyRowRef : undefined}
                onClick={onRowClick ? () => onRowClick(r) : undefined}
                className={`hover:bg-zinc-50 dark:hover:bg-zinc-700/40 ${onRowClick ? 'cursor-pointer' : ''}`}
              >
                {columns.map((c) => (
                  <td
                    key={c.key}
                    className={`px-2.5 py-2 border-b border-zinc-100 dark:border-zinc-700/60 whitespace-nowrap ${
                      c.numeric ? 'text-right tabular-nums' : 'text-left'
                    }`}
                  >
                    {renderCell(r, c, maxByKey[c.key])}
                  </td>
                ))}
              </tr>
            ))}
            <SpacerRow height={rowsBelow * rowHeight} columnCount={columns.length} />
          </tbody>
        </table>
      </div>
      {footnote && <div className="text-[11px] text-zinc-400 dark:text-zinc-500 mt-2">{footnote}</div>}
    </div>
  );
}

/** Stands in for the rows outside the window, so the scrollbar measures the whole list. Hidden from
 *  assistive technology, which reads the rows themselves and has nothing to read here. */
function SpacerRow({ height, columnCount }: { height: number; columnCount: number }) {
  if (height <= 0) return null;
  return (
    <tr aria-hidden="true">
      <td colSpan={columnCount} style={{ height, padding: 0, border: 0 }} />
    </tr>
  );
}

function renderCell(row: Row, col: TableColumn, max?: number) {
  const raw = row[col.key];
  if (col.render === 'id') {
    return <span className="font-mono text-[11.5px] text-zinc-600 dark:text-zinc-300">{String(raw ?? '')}</span>;
  }
  if (col.render === 'badge') {
    return (
      <span className={`text-[10.5px] font-bold px-2 py-0.5 rounded-full ${badgeTone(String(raw ?? ''))}`}>
        {String(raw ?? '')}
      </span>
    );
  }
  if (col.render === 'agebar') {
    const n = Number(raw) || 0;
    const frac = max ? n / max : 0;
    const color = frac > 0.66 ? 'var(--crit)' : frac > 0.4 ? 'var(--warn)' : 'var(--good)';
    return (
      <span className="inline-flex items-center gap-2 justify-end">
        {formatNumber(n, col.format)}
        <span
          className="inline-block h-1.5 rounded-full align-middle"
          style={{ width: `${Math.max(4, frac * 34)}px`, background: color }}
        />
      </span>
    );
  }
  if (col.numeric) return formatNumber(Number(raw), col.format);
  return String(raw ?? '');
}
