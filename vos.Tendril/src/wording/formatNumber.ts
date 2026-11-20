import type { NumberFormat } from '../types/dashboard';

const formats = new Map<string, Intl.NumberFormat>();

/**
 * A number written the way a language writes it: its grouping, its decimal mark, where its percent
 * sign goes. The digits stay Western in every language, because every count a translation
 * interpolates is written that way, and one line holding two number systems reads as two numbers.
 */
export function numberIn(value: number, language: string, options: Intl.NumberFormatOptions = {}): string {
  const key = `${language}|${JSON.stringify(options)}`;
  let format = formats.get(key);
  if (!format) {
    format = new Intl.NumberFormat(language, { numberingSystem: 'latn', ...options });
    formats.set(key, format);
  }
  return format.format(value);
}

/** A figure in the format a page description names for it, or a dash where there is no figure. */
export function formatNumberIn(value: number | null | undefined, format: NumberFormat | undefined, language: string): string {
  if (value === null || value === undefined || isNaN(value)) return '—';
  switch (format) {
    case 'integer':
      return numberIn(Math.round(value), language);
    case 'decimal1':
      return numberIn(value, language, { minimumFractionDigits: 1, maximumFractionDigits: 1 });
    case 'decimal2':
      return numberIn(value, language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    case 'percent':
      return numberIn(value, language, { style: 'percent', maximumFractionDigits: 0 });
    case 'percent1':
      return numberIn(value, language, { style: 'percent', minimumFractionDigits: 1, maximumFractionDigits: 1 });
    case 'percentOutOf100':
      return numberIn(value / 100, language, { style: 'percent', maximumFractionDigits: 0 });
    case 'hours':
      return `${numberIn(value, language, { minimumFractionDigits: 1, maximumFractionDigits: 1 })} h`;
    case 'money':
      return numberIn(value, language, { style: 'currency', currency: 'USD' });
    case 'compact':
      return numberIn(value, language, { notation: 'compact', maximumFractionDigits: 1 });
    case 'bytes':
      return formatBytes(value, language);
    default:
      return numberIn(value, language);
  }
}

const BYTE_UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB', 'PiB'];
const BYTE_FRACTION_DIGITS = 1;

function roundedAsShown(figure: number): number {
  return Math.round(figure * 10 ** BYTE_FRACTION_DIGITS) / 10 ** BYTE_FRACTION_DIGITS;
}

function formatBytes(value: number, language: string): string {
  const size = Math.abs(value);
  let unitIndex = 0;
  while (unitIndex < BYTE_UNITS.length - 1 && roundedAsShown(size / 1024 ** unitIndex) >= 1024) {
    unitIndex++;
  }
  const figure = numberIn(value / 1024 ** unitIndex, language, { maximumFractionDigits: BYTE_FRACTION_DIGITS });
  return `${figure} ${BYTE_UNITS[unitIndex]}`;
}
