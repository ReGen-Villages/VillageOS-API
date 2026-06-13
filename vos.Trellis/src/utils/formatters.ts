import { format, formatDistanceToNow, parseISO } from 'date-fns';

export function formatGuid(guid: string): string {
  return guid.substring(0, 8) + '...';
}

export function formatTimestamp(iso: string): string {
  try {
    return format(parseISO(iso), 'HH:mm:ss');
  } catch {
    return iso;
  }
}

export function formatDateTime(iso: string): string {
  try {
    return format(parseISO(iso), 'yyyy-MM-dd HH:mm:ss');
  } catch {
    return iso;
  }
}

export function formatRelativeTime(iso: string): string {
  try {
    return formatDistanceToNow(parseISO(iso), { addSuffix: true });
  } catch {
    return iso;
  }
}

export function formatMs(ms: number): string {
  if (ms < 1) return '<1ms';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

export function formatPropertyValue(value: unknown): string {
  if (value === null || value === undefined) return '(null)';
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}
