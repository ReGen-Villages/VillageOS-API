import { useState } from 'react';
import { Search, X } from 'lucide-react';
import type { TableWidget, Widget } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';
import { KpiCard } from './KpiCard';
import { Funnel } from './Funnel';
import { BulletChart } from './BulletChart';
import { Gantt } from './Gantt';
import { Leaderboard } from './Leaderboard';
import { ExceptionBar } from './ExceptionBar';
import { DataTable } from './DataTable';
import { WidgetCard } from './WidgetCard';

/** Renders a single widget by its `type`. The only place that knows the widget union.
 *  `openDetail`, when provided, lets row-clickable widgets open a Thing detail window. */
export function WidgetRenderer({
  widget,
  ctx,
  openDetail,
}: {
  widget: Widget;
  ctx: ResolveContext;
  openDetail?: (thingId: string) => void;
}) {
  switch (widget.type) {
    case 'kpi':
      return <KpiCard widget={widget} ctx={ctx} />;
    case 'funnel':
      return <Funnel widget={widget} ctx={ctx} openDetail={openDetail} />;
    case 'bullet':
      return <BulletChart widget={widget} ctx={ctx} />;
    case 'gantt':
      return <Gantt widget={widget} ctx={ctx} />;
    case 'leaderboard':
      return <Leaderboard widget={widget} ctx={ctx} />;
    case 'exceptionBar':
      return <ExceptionBar widget={widget} ctx={ctx} />;
    case 'table':
      return <TableWidgetView widget={widget} ctx={ctx} openDetail={openDetail} />;
    default:
      return null;
  }
}

function TableWidgetView({
  widget,
  ctx,
  openDetail,
}: {
  widget: TableWidget;
  ctx: ResolveContext;
  openDetail?: (thingId: string) => void;
}) {
  const [query, setQuery] = useState('');
  const onRowClick = widget.rowDetail && openDetail ? (row: Row) => openDetail(String(row.id)) : undefined;

  const searchBox = widget.searchable ? (
    <div className="flex items-center gap-1.5 bg-zinc-100 dark:bg-zinc-900/50 rounded px-2 py-1 w-44">
      <Search size={12} className="text-zinc-400 flex-shrink-0" />
      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder="Search…"
        className="bg-transparent text-xs text-zinc-700 dark:text-zinc-200 placeholder-zinc-400 dark:placeholder-zinc-600 focus:outline-none flex-1 min-w-0"
      />
      {query && (
        <button onClick={() => setQuery('')} className="text-zinc-400 hover:text-zinc-600 dark:hover:text-zinc-300 flex-shrink-0">
          <X size={12} />
        </button>
      )}
    </div>
  ) : undefined;

  return (
    <WidgetCard title={widget.title} hint={widget.hint} right={searchBox}>
      <DataTable
        columns={widget.columns}
        rowsBinding={widget.rows}
        ctx={ctx}
        minWidth={widget.minWidth}
        sortKey={widget.sortKey}
        sortDir={widget.sortDir}
        query={widget.searchable ? query : undefined}
        searchKeys={widget.searchKeys}
        onRowClick={onRowClick}
      />
    </WidgetCard>
  );
}
