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
