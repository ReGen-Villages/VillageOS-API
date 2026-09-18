/**
 * What the chart widgets read off a `history` binding and its answer: the groups by key, the window
 * the binding covers, and the calendar words the axes are labelled with. Shared so every chart reads
 * a series the same way and names a month the same way.
 */
import { asRows, nullableNumber, type BindingResult } from '../../../api/dashboardApi';
import type { Binding } from '../../../types/dashboard';

const SECONDS_PER_DAY = 86_400;
const SECONDS_PER_YEAR = 365 * SECONDS_PER_DAY;

/** The reach of a history binding, as a chart labels it: whole years once it spans a year, days
 *  under that. Nothing for a binding of another kind. */
export function windowOf(binding: Binding | undefined): { unit: 'years' | 'days'; count: number } | null {
  if (binding?.kind !== 'history') return null;
  const seconds = binding.windowSeconds;
  return seconds >= SECONDS_PER_YEAR
    ? { unit: 'years', count: Math.round(seconds / SECONDS_PER_YEAR) }
    : { unit: 'days', count: Math.round(seconds / SECONDS_PER_DAY) };
}

/** The groups a history binding resolved to, by key, in the platform's order. A row whose value is
 *  not a number is left out rather than read as zero. */
export function groupsByKey(result: BindingResult): Map<string, number> {
  const groups = new Map<string, number>();
  for (const row of asRows(result)) {
    const value = nullableNumber(row.value);
    if (typeof row.key === 'string' && value !== null) groups.set(row.key, value);
  }
  return groups;
}

/** The twelve month names in the reader's language, abbreviated, January first. */
export function monthNames(locale: string): string[] {
  const format = new Intl.DateTimeFormat(locale, { month: 'short', timeZone: 'UTC' });
  return Array.from({ length: 12 }, (_, month) => format.format(new Date(Date.UTC(2001, month, 1))));
}

/** A leap year, so the 366th day of a fold has a date to be named by. */
const LEAP_YEAR = 2000;

/** A day of the year as a date in the reader's language — "Jan 1", "1 janv." — with no year, since a
 *  fold by day of year lays every year over the same days. */
export function dayOfYearLabel(dayOfYear: number, locale: string): string {
  const format = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', timeZone: 'UTC' });
  return format.format(new Date(Date.UTC(LEAP_YEAR, 0, dayOfYear)));
}

/** The first day of the year of each month, for an axis over days of the year. */
export function monthStarts(): number[] {
  return Array.from({ length: 12 }, (_, month) =>
    Math.round((Date.UTC(LEAP_YEAR, month, 1) - Date.UTC(LEAP_YEAR, 0, 1)) / 86_400_000) + 1);
}
