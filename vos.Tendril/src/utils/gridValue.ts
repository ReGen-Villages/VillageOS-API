import type { GridValue } from '../types/vos';

const GRID_NUMBERS = ['originLatitude', 'originLongitude', 'cellSizeMetres', 'latitudeStepDegrees', 'longitudeStepDegrees'] as const;

/** A `vos.Grid` as the platform writes it, or nothing for a value that is not one. */
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

export function cellsIn(grid: GridValue): number {
  return grid.rows * grid.columns;
}
