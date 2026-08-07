import { format, formatDistanceToNow, parseISO } from 'date-fns';
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
  try {
    return formatDistanceToNow(parseISO(iso), { addSuffix: true });
  } catch {
    return iso;
  }
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  let value = bytes;
  let unit = 'B';
  for (const next of ['KB', 'MB', 'GB', 'TB']) {
    if (value < 1024) break;
    value /= 1024;
    unit = next;
  }
  return `${value.toFixed(1)} ${unit}`;
}

export function formatMs(ms: number): string {
  if (ms < 1) return '<1ms';
  if (ms < 1000) return `${Math.round(ms)}ms`;
  return `${(ms / 1000).toFixed(1)}s`;
}

/**
 * A property value as a reader should see it, given what the platform says the property holds.
 *
 * Without a declared type it falls back to the value's own shape, which is all this could ever do
 * before. A type this build does not recognise is a reason to fall back, not to hide the value:
 * a badly formatted value is recoverable, a missing one is not.
 */
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

/** Inline mesh data is thousands of coordinates. Its size is what a reader can act on; the
 *  coordinates themselves are for the 3D viewer, not for a cell in a side panel. */
function summariseMesh(value: unknown): string {
  const mesh = value as { positions?: unknown[]; indices?: unknown[] };
  if (!Array.isArray(mesh?.positions)) return 'mesh';
  const triangles = Array.isArray(mesh.indices) ? `, ${mesh.indices.length / 3} triangles` : '';
  return `mesh (${mesh.positions.length / 3} vertices${triangles})`;
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
