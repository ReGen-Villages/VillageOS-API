import i18n from './index';

const formats = new Map<string, Intl.NumberFormat>();

/**
 * A number written the way the chosen language writes it: its grouping, its decimal mark, where its
 * percent sign goes. The digits stay Western in every language, because every count the translations
 * interpolate is written that way, and one line holding two number systems reads as two numbers.
 */
export function numberIn(value: number, options: Intl.NumberFormatOptions = {}): string {
  const key = `${i18n.language}|${JSON.stringify(options)}`;
  let format = formats.get(key);
  if (!format) {
    format = new Intl.NumberFormat(i18n.language, { numberingSystem: 'latn', ...options });
    formats.set(key, format);
  }
  return format.format(value);
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
