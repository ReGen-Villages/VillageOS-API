import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Search, X } from 'lucide-react';
import type { TableWidget, Widget } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';
import { unimplementedWordsIn } from '../../../api/bindingVocabulary';
import { KpiCard } from './KpiCard';
import { Funnel } from './Funnel';
import { BulletChart } from './BulletChart';
import { Gantt } from './Gantt';
import { Leaderboard } from './Leaderboard';
import { VerdictList } from './VerdictList';
import { WorkingList } from './WorkingList';
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
  // Asked before anything resolves, because a widget drawn from a question this build only half
  // understands shows a figure rather than a gap — and a figure is read as an answer.
  const unanswered = unimplementedWordsIn(widget);
  if (unanswered.length > 0) return <UnknownWidget reason={{ unanswered }} />;

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
    case 'verdict':
      return <VerdictList widget={widget} ctx={ctx} />;
    case 'working':
      return <WorkingList widget={widget} ctx={ctx} />;
    case 'exceptionBar':
      return <ExceptionBar widget={widget} ctx={ctx} />;
    case 'table':
      return <TableWidgetView widget={widget} ctx={ctx} openDetail={openDetail} />;
    default:
      return <UnknownWidget reason={{ unknownType: (widget as { type?: unknown }).type }} />;
  }
}

/** A widget this build did not draw, and which of the two reasons it was. The spec is model data, so
 *  it can name a kind that was misspelled or added after this client shipped, and it can ask a
 *  question in vocabulary this client has no answer for; the view draws everything else and says
 *  what it left out, rather than leaving a silent hole an author cannot account for. */
function UnknownWidget({ reason }: { reason: { unknownType: unknown } | { unanswered: string[] } }) {
  const { t } = useTranslation();
  // A spec can leave the word out as easily as misspell it, and either way the reader is owed a
  // sentence in their own language rather than a gap where the word would be.
  const named = (word: unknown) =>
    typeof word === 'string' && word !== '' ? word : t('widgets.unknown.noKind');
  const body = 'unanswered' in reason
    ? t('widgets.unknown.unanswerable', { words: reason.unanswered.map(named).join(', ') })
    : t('widgets.unknown.body', { kind: named(reason.unknownType) });
  return (
    <WidgetCard title={t('widgets.unknown.title')}>
      <p className="mt-2 text-[13px] leading-relaxed text-zinc-500 dark:text-zinc-400">{body}</p>
    </WidgetCard>
  );
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
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  const onRowClick = widget.rowDetail && openDetail ? (row: Row) => openDetail(String(row.id)) : undefined;

  const searchBox = widget.searchable ? (
    <div className="flex items-center gap-1.5 bg-zinc-100 dark:bg-zinc-900/50 rounded px-2 py-1 w-44">
      <Search size={12} className="text-zinc-400 flex-shrink-0" />
      <input
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        placeholder={t('widgets.table.searchPlaceholder')}
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
        visibleRows={widget.visibleRows}
        query={widget.searchable ? query : undefined}
        searchKeys={widget.searchKeys}
        onRowClick={onRowClick}
      />
    </WidgetCard>
  );
}
