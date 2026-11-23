/**
 * What a profile reads as: its values at points along a line, and their lowest, highest and difference in
 * words. The one place the console's profile chart and a reader with no screen take these from.
 */
import { asNumber, type BindingResult } from '../api/dashboardApi';
import type { NumberFormat } from '../types/dashboard';
import { gridOf } from '../utils/gridValue';
import { formatNumberIn } from './formatNumber';

export interface ProfilePoint {
  distanceMetres: number;
  value: number | null;
}

export interface ProfileFigures {
  lowest: number;
  highest: number;
  difference: number;
}

/** A one-row grid runs west to east and a one-column grid south to north, a cell's size apart. */
export function profilePoints(value: unknown): ProfilePoint[] | null {
  const grid = gridOf(value);
  if (!grid || (grid.rows !== 1 && grid.columns !== 1)) return null;
  return grid.values.map((value, at) => ({ distanceMetres: at * grid.cellSizeMetres, value }));
}

/** The span in metres along the line, or none unless both its ends are numbers. */
export function profileSpanOf(from: BindingResult | undefined, to: BindingResult | undefined): { from: number; to: number } | null {
  const start = asNumber(from ?? null);
  const end = asNumber(to ?? null);
  return start !== null && end !== null ? { from: start, to: end } : null;
}

export function profileFigures(points: ProfilePoint[], span: { from: number; to: number } | null): ProfileFigures | null {
  let lowest = Infinity;
  let highest = -Infinity;
  for (const point of points) {
    if (point.value === null) continue;
    if (span && (point.distanceMetres < span.from || point.distanceMetres > span.to)) continue;
    if (point.value < lowest) lowest = point.value;
    if (point.value > highest) highest = point.value;
  }
  return lowest <= highest ? { lowest, highest, difference: highest - lowest } : null;
}

export function profileWords(
  figures: ProfileFigures, format: NumberFormat | undefined, unit: string | undefined, language: string,
): Record<keyof ProfileFigures, string> {
  const worded = (value: number) => `${formatNumberIn(value, format, language)}${unit ? ` ${unit}` : ''}`;
  return { lowest: worded(figures.lowest), highest: worded(figures.highest), difference: worded(figures.difference) };
}
