import type { GridValue } from '@tendril-ui/tendril';

const GRID_NUMBERS = ['originLatitude', 'originLongitude', 'cellSizeMetres', 'latitudeStepDegrees', 'longitudeStepDegrees'] as const;

export function gridOf(value: unknown): GridValue | null {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
  const held = value as Record<string, unknown>;
  if (GRID_NUMBERS.some((name) => typeof held[name] !== 'number' || !Number.isFinite(held[name]))) return null;
  if ((held.latitudeStepDegrees as number) <= 0 || (held.longitudeStepDegrees as number) <= 0) return null;
  const { rows, columns, values } = held;
  if (!Number.isInteger(rows) || !Number.isInteger(columns) || (rows as number) < 1 || (columns as number) < 1) return null;
  if (!Array.isArray(values) || values.length !== (rows as number) * (columns as number)) return null;
  if (!values.every((cell) => cell === null || (typeof cell === 'number' && Number.isFinite(cell)))) return null;
  return held as unknown as GridValue;
}

export interface CellBounds {
  south: number;
  north: number;
  west: number;
  east: number;
}

/** Each value was sampled at a point, so its cell reaches half a step either side of it. */
export function cellBounds(grid: GridValue, row: number, column: number): CellBounds {
  const latitude = grid.originLatitude + row * grid.latitudeStepDegrees;
  const longitude = grid.originLongitude + column * grid.longitudeStepDegrees;
  const round = (degrees: number) => Number(degrees.toFixed(9));
  return {
    south: round(latitude - grid.latitudeStepDegrees / 2),
    north: round(latitude + grid.latitudeStepDegrees / 2),
    west: round(longitude - grid.longitudeStepDegrees / 2),
    east: round(longitude + grid.longitudeStepDegrees / 2),
  };
}

/** Web Mercator on the unit square: x from the antimeridian eastward, y from the north downward. */
export function mercator(latitude: number, longitude: number): { x: number; y: number } {
  const radians = (latitude * Math.PI) / 180;
  return {
    x: (longitude + 180) / 360,
    y: 0.5 - Math.log(Math.tan(Math.PI / 4 + radians / 2)) / (2 * Math.PI),
  };
}

/** A logarithmic scale has no place for nought or below, so those sit at its floor; its floor must be above nought. */
export function shareOnScale(value: number, floor: number, ceiling: number, scale: 'linear' | 'logarithmic'): number {
  if (scale === 'logarithmic' && (value <= 0 || floor <= 0)) return 0;
  const place = (reading: number) => (scale === 'logarithmic' ? Math.log10(reading) : reading);
  const span = place(ceiling) - place(floor);
  if (span <= 0) return 0;
  return Math.min(1, Math.max(0, (place(value) - place(floor)) / span));
}
