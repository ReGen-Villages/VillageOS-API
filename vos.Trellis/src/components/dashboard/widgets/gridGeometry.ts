import type { GridValue } from '@tendril-ui/tendril';

/** Above this a grid map draws no cells. Measured in Chrome on a production build, a first draw at this count
 *  stays well under half a second, while a grid at the platform's own limit freezes the page for seconds. */
export const MOST_CELLS_A_MAP_DRAWS = 10_000;

export function cellsIn(grid: GridValue): number {
  return grid.rows * grid.columns;
}

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

/** Walked rather than spread into `Math.min`, which overflows the call stack on a large grid's corners. */
export function extentOf(numbers: number[]): { lowest: number; highest: number } | null {
  if (numbers.length === 0) return null;
  let lowest = Infinity;
  let highest = -Infinity;
  for (const number of numbers) {
    if (number < lowest) lowest = number;
    if (number > highest) highest = number;
  }
  return { lowest, highest };
}

type Ring = [number, number][];

function isRing(value: unknown): value is Ring {
  return Array.isArray(value) && value.length >= 3
    && value.every((point) => Array.isArray(point) && typeof point[0] === 'number' && typeof point[1] === 'number');
}

/** The outer ring of each area a GeoJSON Polygon, MultiPolygon or Feature holding one describes; holes are left out. */
export function outerRingsOf(value: unknown): Ring[] {
  if (value === null || typeof value !== 'object') return [];
  const { type, coordinates, geometry } = value as { type?: unknown; coordinates?: unknown; geometry?: unknown };
  if (type === 'Feature') return outerRingsOf(geometry);
  if (type === 'Polygon' && Array.isArray(coordinates) && isRing(coordinates[0])) return [coordinates[0]];
  if (type === 'MultiPolygon' && Array.isArray(coordinates))
    return coordinates.flatMap((polygon) => (Array.isArray(polygon) && isRing(polygon[0]) ? [polygon[0]] : []));
  return [];
}

/** A logarithmic scale has no place for nought or below, so those sit at its floor; its floor must be above nought. */
export function shareOnScale(value: number, floor: number, ceiling: number, scale: 'linear' | 'logarithmic'): number {
  if (scale === 'logarithmic' && (value <= 0 || floor <= 0)) return 0;
  const place = (reading: number) => (scale === 'logarithmic' ? Math.log10(reading) : reading);
  const span = place(ceiling) - place(floor);
  if (span <= 0) return 0;
  return Math.min(1, Math.max(0, (place(value) - place(floor)) / span));
}
