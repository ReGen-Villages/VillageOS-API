import type { NumberFormat } from '../../../types/dashboard';

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
