import { numberIn as numberInALanguage } from '@tendril-ui/tendril';
import i18n from './index';

/** A number written the way the chosen language writes it, with Western digits in every language. */
export function numberIn(value: number, options: Intl.NumberFormatOptions = {}): string {
  return numberInALanguage(value, i18n.language, options);
}

const RELATIVE_UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 3600],
  ['month', 30 * 24 * 3600],
  ['week', 7 * 24 * 3600],
  ['day', 24 * 3600],
  ['hour', 3600],
  ['minute', 60],
  ['second', 1],
];

/** How long ago, or how long until, in the chosen language: "3 hours ago", "vor 3 Stunden". */
export function relativeTimeIn(at: Date, now: Date = new Date()): string {
  const seconds = (at.getTime() - now.getTime()) / 1000;
  const [unit, size] = RELATIVE_UNITS.find(([, length]) => Math.abs(seconds) >= length) ?? RELATIVE_UNITS[RELATIVE_UNITS.length - 1];
  return new Intl.RelativeTimeFormat(i18n.language, { numeric: 'auto', numberingSystem: 'latn' } as Intl.RelativeTimeFormatOptions)
    .format(Math.round(seconds / size), unit);
}
