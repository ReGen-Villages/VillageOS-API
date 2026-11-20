import { format, parseISO } from 'date-fns';
import i18n from '../i18n';
import { numberIn, relativeTimeIn } from '../i18n/numbers';
import { NUMBER_DISPLAY_DEFAULTS, type NumberDisplaySettings } from './guiSettings';

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
  const at = parseISO(iso);
  return Number.isNaN(at.getTime()) ? iso : relativeTimeIn(at);
}

const ONE_DECIMAL: Intl.NumberFormatOptions = { minimumFractionDigits: 1, maximumFractionDigits: 1 };

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  let value = bytes;
  let unit = 'B';
  for (const next of ['KB', 'MB', 'GB', 'TB']) {
    if (value < 1024) break;
    value /= 1024;
    unit = next;
  }
  return `${numberIn(value, ONE_DECIMAL)} ${unit}`;
}

export function formatMilliseconds(milliseconds: number): string {
  if (milliseconds < 1) return '<1ms';
  if (milliseconds < 1000) return `${Math.round(milliseconds)}ms`;
  return `${numberIn(milliseconds / 1000, ONE_DECIMAL)}s`;
}

/** Without a declared type, or with one this build does not know, the value's own shape decides, because a
 *  badly formatted value is recoverable and a missing one is not. */
export function formatPropertyValue(
  value: unknown,
  type?: string,
  numbers: NumberDisplaySettings = NUMBER_DISPLAY_DEFAULTS,
): string {
  if (value === null || value === undefined) return '(null)';

  switch (type) {
    case 'vos.DateTime':
      return typeof value === 'string' ? formatDateTime(value) : String(value);
    case 'vos.Guid':
      return typeof value === 'string' ? formatGuid(value) : String(value);
    case 'vos.Double':
    case 'vos.Float':
      return formatToPrecision(value, numbers.floatingPointPrecision);
    case 'vos.Decimal':
      return formatToPrecision(value, numbers.decimalPrecision);
    case 'vos.IfcGeometry':
      return summariseMesh(value);
    case 'vos.GeoJson':
      return summariseGeoJson(value);
  }

  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

/** Decimal places are a maximum, not a width: a reading that needs fewer does not show padding. */
function formatToPrecision(value: unknown, maximumDecimals: number): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) return String(value);
  const fixed = value.toFixed(maximumDecimals);
  return fixed.includes('.') ? fixed.replace(/0+$/, '').replace(/\.$/, '') : fixed;
}

/** Mesh data is a great many coordinates, which belong to the 3D viewer; its size is what a reader can use. */
function summariseMesh(value: unknown): string {
  const mesh = value as { positions?: unknown[]; indices?: unknown[] };
  if (!Array.isArray(mesh?.positions)) return i18n.t('common.mesh');
  const vertices = mesh.positions.length / 3;
  return Array.isArray(mesh.indices)
    ? i18n.t('common.meshSizeWithTriangles', { vertices, triangles: mesh.indices.length / 3 })
    : i18n.t('common.meshSize', { vertices });
}

/** The shape's own type word — data rather than prose, and the one thing a reader wants from a
 *  body of coordinates they cannot read at this size. */
function summariseGeoJson(value: unknown): string {
  const wrapper = value as { Json?: unknown };
  if (typeof wrapper?.Json !== 'string') return 'GeoJson';
  try {
    const shape = JSON.parse(wrapper.Json) as { type?: unknown };
    return typeof shape?.type === 'string' ? shape.type : 'GeoJson';
  } catch {
    return 'GeoJson';
  }
}
