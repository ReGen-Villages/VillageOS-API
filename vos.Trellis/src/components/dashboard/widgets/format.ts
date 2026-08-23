import type { NumberFormat, TableColumn } from '../../../types/dashboard';
import type { OriginKind } from '../../../types/vos';
import type { Row } from '../../../api/dashboardApi';

/** Format a numeric value for display per a widget's declared NumberFormat. */
export function formatNumber(value: number | null | undefined, fmt?: NumberFormat): string {
  if (value === null || value === undefined || isNaN(value)) return '—';
  switch (fmt) {
    case 'integer':
      return Math.round(value).toLocaleString('en-US');
    case 'decimal1':
      return value.toFixed(1);
    case 'decimal2':
      return value.toFixed(2);
    case 'percent':
      return `${Math.round(value * 100)}%`;
    case 'percent1':
      return `${(value * 100).toFixed(1)}%`;
    case 'pct100':
      return `${Math.round(value)}%`;
    case 'hours':
      return `${value.toFixed(1)} h`;
    case 'money':
      return `$${value.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
    case 'compact':
      if (Math.abs(value) >= 1000) return `${(value / 1000).toFixed(1)}k`;
      return `${Math.round(value)}`;
    default:
      return value.toLocaleString('en-US');
  }
}

/** Tailwind classes colouring a status/state pill by its wording. Shared by the table
 *  `badge` cell and the detail window's derived-state pills so tones stay consistent.
 *
 *  The vocabulary is deliberately domain-neutral status wording only: a model's own terms
 *  must not be listed here, since Trellis renders any model. A term it cannot read
 *  generically falls through to the neutral tone until the model supplies its own
 *  mapping (#5962). */
export function badgeTone(value: string): string {
  const v = value.toLowerCase();
  if (/crit|overdue|fail|error|out|held|block|below/.test(v))
    return 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400';
  if (/warn|risk|high|tight|over/.test(v))
    return 'bg-amber-100 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400';
  if (/good|track|ok|normal|done|ship|complete/.test(v))
    return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400';
  return 'bg-zinc-100 text-zinc-600 dark:bg-zinc-700 dark:text-zinc-300';
}

/** Tailwind classes colouring an origin line by where the value came from. Four origins, four
 *  tones, so a submitted figure and a fetched one are told apart before either is read. The tones
 *  say "different", not "better": an assumption and an unrecorded origin are the two a reader has
 *  to notice, so they carry the two that stand out. The words beside them are the model's, and
 *  carry the same distinction for a reader who cannot tell the colours apart. */
export function originTone(origin: OriginKind): string {
  switch (origin) {
    case 'stated': return 'text-zinc-400 dark:text-zinc-500';
    case 'measured': return 'text-sky-600 dark:text-sky-400';
    case 'assumed': return 'text-amber-600 dark:text-amber-500';
    case 'unknown': return 'text-rose-600 dark:text-rose-400';
  }
}

/** Signed delta with a leading arrow, e.g. "▲ 6.1". */
export function formatDelta(value: number | null | undefined, fmt?: NumberFormat): string {
  if (value === null || value === undefined || isNaN(value)) return '';
  const arrow = value > 0 ? '▲' : value < 0 ? '▼' : '•';
  return `${arrow} ${formatNumber(Math.abs(value), fmt)}`;
}

/** 'up' when the delta is favourable given the metric's good-direction. */
export function deltaTone(
  value: number | null | undefined,
  direction: 'up-good' | 'down-good' = 'up-good',
): 'up' | 'down' | 'flat' {
  if (value === null || value === undefined || isNaN(value) || value === 0) return 'flat';
  const rising = value > 0;
  const good = direction === 'up-good' ? rising : !rising;
  return good ? 'up' : 'down';
}

/** The largest value in each `agebar` column, which that column's bars are drawn as a fraction of.
 *  A loop rather than `Math.max(...values)`: a roster no longer needs a row cap to keep a page
 *  responsive, so the list reaching here has no bound, and spreading a long one over a call
 *  overflows the stack somewhere past a hundred thousand rows. */
export function columnMaxima(rows: Row[], columns: TableColumn[]): Record<string, number> {
  const maxima: Record<string, number> = {};
  for (const column of columns) {
    if (column.render !== 'agebar') continue;
    let largest = 1;
    for (const row of rows) {
      const value = Number(row[column.key]) || 0;
      if (value > largest) largest = value;
    }
    maxima[column.key] = largest;
  }
  return maxima;
}
