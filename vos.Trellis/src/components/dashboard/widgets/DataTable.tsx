import { useMemo, useState } from 'react';
import type { Binding, TableColumn } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';
import { asRows, filterRows } from '../../../api/dashboardApi';
import { useBinding } from '../../../hooks/useDashboard';
import { useElementHeight } from '../../../hooks/useElementHeight';
import { formatNumber, badgeTone } from './format';

/* Body row height for the visibleRows cap, derived from the row classes below: the 1.5 line box
   at the table font, plus py-2 padding and the bottom border. In em, so it tracks the font size.
   The header is measured instead of derived — its labels wrap in narrow columns, and column
   widths depend on the data. */
const BODY_ROW_HEIGHT = '(1.5em + 1rem + 1px)';

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

  const maxByKey = useMemo(() => {
    const m: Record<string, number> = {};
    for (const c of columns) {
      if (c.render === 'agebar') m[c.key] = Math.max(1, ...rows.map((r) => Number(r[c.key]) || 0));
    }
    return m;
  }, [rows, columns]);

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
        className={`overflow-x-auto text-[12.5px]${visibleRows ? ' overflow-y-auto' : ''}`}
        style={visibleRows ? { maxHeight: `calc(${headerHeight}px + ${visibleRows} * ${BODY_ROW_HEIGHT})` } : undefined}
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
            {sorted.map((r, i) => (
              <tr
                key={(r.id as string) ?? i}
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
          </tbody>
        </table>
      </div>
      {footnote && <div className="text-[11px] text-zinc-400 dark:text-zinc-500 mt-2">{footnote}</div>}
    </div>
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
