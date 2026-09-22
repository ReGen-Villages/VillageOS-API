/**
 * A figure opened up: the whole viewport given over to what one number is made of.
 *
 * The dashboard's one structural mark is a dotted rule under a figure the model derived; this is
 * what the rule opens. It shows the figure, the model's own words for what it names, and the Things
 * it was formed from — each row opening the detail card that already exists, so the breakdown
 * reaches the whole model without listing any of it itself.
 */
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { X } from 'lucide-react';
import type { Binding, NumberFormat } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';
import type { BehindTheFigure, FigureBreakdown, FigureTerms } from '../../../api/figureBreakdown';
import { useElementWidth } from '../../../hooks/useElementWidth';
import { useFigureBreakdown } from '../../../hooks/useFigureBreakdown';
import { breakdownTable } from './breakdownTable';
import { DataTable } from './DataTable';
import { SearchBox } from './SearchBox';
import { Sparkline } from './Sparkline';
import { formatNumber } from './format';

/** Rows kept in the document at once. The rest scroll into it — a state count can name every Thing
 *  of its archetype, and a full-height table of them all is a page that takes a second to paint. */
const VISIBLE_ROWS = 18;

/** The same, for one side of a division. Shorter so both sides are on screen together, which is the
 *  whole reason a reader opens a rate rather than either of the figures it is made of. */
const VISIBLE_ROWS_PER_SIDE = 7;

/** Width the trace is drawn at before the panel has been measured, and the floor a narrow one keeps. */
const MINIMUM_TRACE_WIDTH = 320;

/** What each reduction is called in the sentence saying how the rows became the figure. `count` and
 *  `read` are absent because neither reads as a word in that sentence — each has a sentence of its
 *  own. */
const REDUCTION_WORDS = {
  sum: 'breakdown.reduction.sum',
  avg: 'breakdown.reduction.avg',
  min: 'breakdown.reduction.min',
  max: 'breakdown.reduction.max',
} as const;

export function FigurePanel({
  title,
  binding,
  context,
  format,
  unit,
  footnote,
  onClose,
  openDetail,
}: {
  title?: string;
  binding: Binding;
  context: ResolveContext;
  format?: NumberFormat;
  unit?: string;
  footnote?: string;
  onClose: () => void;
  openDetail?: (thingId: string) => void;
}) {
  const { t } = useTranslation();
  const { loading, breakdown } = useFigureBreakdown(binding, context);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => event.key === 'Escape' && onClose();
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div
      className="fixed inset-0 z-30 flex items-center p-2 sm:p-4"
      role="dialog"
      aria-modal="true"
      aria-label={title ?? t('breakdown.title')}
    >
      <div className="absolute inset-0 bg-zinc-900/40 dark:bg-black/60" onClick={onClose} />
      {/* Tall enough for what it holds and no taller: a figure made of a few parts would otherwise
          leave most of the viewport blank to say so. A long list still fills it and scrolls. */}
      <div className="relative flex-1 min-w-0 max-h-full flex flex-col rounded-xl bg-white dark:bg-zinc-900 border border-zinc-200 dark:border-zinc-700 shadow-2xl overflow-hidden">
        <header className="flex-shrink-0 flex items-start justify-between gap-6 px-6 py-4 border-b border-zinc-200 dark:border-zinc-700">
          <div className="min-w-0">
            <h2 className="text-[11px] font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">{title}</h2>
            <div className="text-4xl font-bold tracking-tight tabular-nums text-zinc-900 dark:text-white leading-none mt-1.5">
              {formatNumber(breakdown?.value ?? null, format)}
              {unit && <span className="text-base text-zinc-400 dark:text-zinc-500 font-semibold ml-1.5">{unit}</span>}
            </div>
            {footnote && <div className="text-[11.5px] text-zinc-400 dark:text-zinc-500 mt-1.5">{footnote}</div>}
          </div>
          <button
            onClick={onClose}
            aria-label={t('breakdown.close')}
            className="flex-shrink-0 text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200 rounded p-1"
          >
            <X size={18} />
          </button>
        </header>

        <div className="min-h-0 overflow-auto px-6 py-5">
          <h3 className="text-[12px] uppercase tracking-wider font-bold text-zinc-400 dark:text-zinc-500 mb-3">
            {t('breakdown.title')}
          </h3>
          {loading ? (
            <div className="py-10 text-center text-xs text-zinc-400">{t('breakdown.reading')}</div>
          ) : breakdown ? (
            <BreakdownView breakdown={breakdown} context={context} format={format} openDetail={openDetail} />
          ) : (
            <div className="py-10 text-center text-xs text-zinc-400">{t('breakdown.nothingToList')}</div>
          )}
        </div>
      </div>
    </div>
  );
}

function BreakdownView({
  breakdown,
  context,
  format,
  visibleRows = VISIBLE_ROWS,
  openDetail,
}: {
  breakdown: FigureBreakdown;
  context: ResolveContext;
  format?: NumberFormat;
  visibleRows?: number;
  openDetail?: (thingId: string) => void;
}) {
  const { t } = useTranslation();
  if (!breakdown.behind) return <div className="text-xs text-zinc-400">{t('breakdown.nothingToList')}</div>;
  return (
    <>
      <TermList terms={breakdown.terms} />
      <Behind behind={breakdown.behind} context={context} format={format} visibleRows={visibleRows} openDetail={openDetail} />
    </>
  );
}

function Behind({
  behind,
  context,
  format,
  visibleRows,
  openDetail,
}: {
  behind: BehindTheFigure;
  context: ResolveContext;
  format?: NumberFormat;
  visibleRows: number;
  openDetail?: (thingId: string) => void;
}) {
  const { t } = useTranslation();

  if (behind.kind === 'things') {
    return (
      <>
        <Lead>
          {behind.reduction === 'count'
            ? t('breakdown.lead.count')
            : behind.reduction === 'read'
              ? t('breakdown.lead.read')
              : t('breakdown.lead.reduced', {
                  measure: behind.measure,
                  reduction: t(REDUCTION_WORDS[behind.reduction]),
                })}
        </Lead>
        <ThingsBehind rows={behind.rows} measure={behind.measure} context={context} visibleRows={visibleRows} openDetail={openDetail} />
      </>
    );
  }

  if (behind.kind === 'buckets') {
    return (
      <>
        <Lead>{t('breakdown.lead.buckets', { count: behind.values.length })}</Lead>
        <BucketTrace behind={behind} format={format} />
      </>
    );
  }

  return (
    <div className="grid gap-5 mt-1" style={{ gridTemplateColumns: 'minmax(0, 1fr)' }}>
      <DivisionSide label={t('breakdown.division.top')} side={behind.numerator} context={context} openDetail={openDetail} />
      <DivisionSide label={t('breakdown.division.bottom')} side={behind.denominator} context={context} openDetail={openDetail} />
    </div>
  );
}

function DivisionSide({
  label,
  side,
  context,
  openDetail,
}: {
  label: string;
  side: FigureBreakdown;
  context: ResolveContext;
  openDetail?: (thingId: string) => void;
}) {
  return (
    <section className="rounded-lg border border-zinc-200 dark:border-zinc-700 p-4">
      <div className="flex items-baseline gap-3 mb-2">
        <h4 className="text-[11px] font-semibold uppercase tracking-wide text-zinc-500 dark:text-zinc-400">{label}</h4>
        <span className="text-xl font-bold tabular-nums text-zinc-900 dark:text-white">{formatNumber(side.value)}</span>
      </div>
      <BreakdownView breakdown={side} context={context} visibleRows={VISIBLE_ROWS_PER_SIDE} openDetail={openDetail} />
    </section>
  );
}

function ThingsBehind({
  rows,
  measure,
  context,
  visibleRows,
  openDetail,
}: {
  rows: Row[];
  measure: string | null;
  context: ResolveContext;
  visibleRows: number;
  openDetail?: (thingId: string) => void;
}) {
  const { t } = useTranslation();
  const [query, setQuery] = useState('');
  // Held across renders: choosing the columns reads every property of every row, and a reader
  // typing in the search box re-renders on each keystroke over rows that have not changed.
  const { columns, omitted } = useMemo(() => breakdownTable(rows, measure, t('breakdown.name')), [rows, measure, t]);

  return (
    <div>
      <div className="flex items-center justify-between gap-3 flex-wrap mb-2">
        <div className="text-[11.5px] text-zinc-500 dark:text-zinc-400">
          {t('breakdown.rowsBehind', { count: rows.length })}
          {openDetail && <span className="text-zinc-400 dark:text-zinc-500"> · {t('breakdown.openRow')}</span>}
        </div>
        <SearchBox value={query} onChange={setQuery} placeholder={t('widgets.table.searchPlaceholder')} className="bg-zinc-100 dark:bg-zinc-800 w-52" />
      </div>
      <DataTable
        columns={columns}
        rows={rows}
        context={context}
        query={query}
        visibleRows={visibleRows}
        sortKey={measure ?? 'name'}
        sortDir={measure ? 'desc' : 'asc'}
        emptyLabel={t('breakdown.empty')}
        onRowClick={openDetail ? (row) => openDetail(String(row.id)) : undefined}
        footnote={omitted.length ? t('breakdown.alsoHeld', { properties: omitted.join(', ') }) : undefined}
      />
    </div>
  );
}

/** The parts a trailing window divides into, oldest first, each labelled with the clock time it ran
 *  to. The labels count back from the moment the platform answered, read off this browser's clock —
 *  the same clock the reader checks the screen against. */
function BucketTrace({
  behind,
  format,
}: {
  behind: Extract<BehindTheFigure, { kind: 'buckets' }>;
  format?: NumberFormat;
}) {
  const parts = behind.values.length;
  const [traceArea, traceWidth] = useElementWidth();
  return (
    <div ref={traceArea}>
      <Sparkline values={behind.values} width={Math.max(traceWidth, MINIMUM_TRACE_WIDTH)} height={110} />
      <div className="mt-3 flex gap-2 flex-wrap">
        {behind.values.map((value, i) => (
          <div
            key={i}
            className="flex-1 min-w-[72px] text-center py-2 rounded-lg bg-zinc-50 dark:bg-zinc-800 border border-zinc-200 dark:border-zinc-700"
          >
            <div className="text-[15px] font-bold tabular-nums text-zinc-800 dark:text-zinc-100">
              {formatNumber(value, format)}
            </div>
            <div className="text-[10px] text-zinc-400 dark:text-zinc-500 tabular-nums">
              {new Date(behind.endsAt - (parts - 1 - i) * behind.bucketSeconds * 1000).toLocaleTimeString(undefined, {
                hour: '2-digit',
                minute: '2-digit',
              })}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

function Lead({ children }: { children: React.ReactNode }) {
  return <p className="text-[12.5px] text-zinc-600 dark:text-zinc-300 mb-3">{children}</p>;
}

/** The model's own words for what the figure names. Never translated, the way state names and
 *  property keys are never translated anywhere else on a dashboard. */
function TermList({ terms }: { terms: FigureTerms }) {
  const { t } = useTranslation();
  const stated: [string, string][] = [];
  if (terms.archetype) stated.push([t('breakdown.terms.archetype'), terms.archetype]);
  if (terms.state) stated.push([t('breakdown.terms.state'), terms.state]);
  if (terms.property) stated.push([t('breakdown.terms.property'), terms.property]);
  if (terms.happenedAt) stated.push([t('breakdown.terms.happenedAt'), terms.happenedAt]);
  if (terms.within) stated.push([t('breakdown.terms.within'), terms.within]);
  for (const filter of terms.where ?? []) {
    stated.push([t('breakdown.terms.where'), `${filter.property} ${filter.op} ${String(filter.value)}`]);
  }
  if (!stated.length) return null;

  return (
    <div className="flex flex-wrap gap-2 mb-3">
      {stated.map(([label, value], i) => (
        <span
          key={`${label}-${i}`}
          className="inline-flex items-baseline gap-1.5 rounded-full bg-zinc-100 dark:bg-zinc-800 px-2.5 py-1"
        >
          <span className="text-[10px] uppercase tracking-wide text-zinc-400 dark:text-zinc-500">{label}</span>
          <span className="text-[11.5px] font-semibold text-zinc-700 dark:text-zinc-200 font-mono">{value}</span>
        </span>
      ))}
    </div>
  );
}
