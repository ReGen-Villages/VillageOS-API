export type FigureRefusal = 'notANumber' | 'negative' | 'notWhole';

export type TypedFigure = { readonly value: number | null } | { readonly refusal: FigureRefusal };

export const FIGURE_REFUSAL_WORDING = {
  notANumber: 'typedFigure.notANumber',
  negative: 'typedFigure.negative',
  notWhole: 'typedFigure.notWhole',
} as const;

export function typedFigure(typed: string, whole: boolean): TypedFigure {
  const text = typed.trim();
  if (text.length === 0) return { value: null };
  const value = Number(text);
  if (!Number.isFinite(value)) return { refusal: 'notANumber' };
  if (value < 0) return { refusal: 'negative' };
  if (whole && !Number.isInteger(value)) return { refusal: 'notWhole' };
  return { value };
}

export interface PeopleFigures {
  readonly population: string;
  readonly householdSize: string;
}

type PeopleFigure = keyof PeopleFigures;

const WHOLE: Readonly<Record<PeopleFigure, boolean>> = { population: true, householdSize: false };

function readEach(held: PeopleFigures): [PeopleFigure, TypedFigure][] {
  return (Object.keys(WHOLE) as PeopleFigure[]).map((field) => [field, typedFigure(held[field], WHOLE[field])]);
}

export function figureRefusals(held: PeopleFigures): Partial<Record<PeopleFigure, FigureRefusal>> {
  return Object.fromEntries(
    readEach(held).flatMap(([field, figure]) => ('refusal' in figure ? [[field, figure.refusal]] : [])),
  );
}

export function everyFigureReads(held: PeopleFigures): boolean {
  return Object.keys(figureRefusals(held)).length === 0;
}

export function figuresFrom(held: PeopleFigures): { population?: number; householdSize?: number } {
  return Object.fromEntries(
    readEach(held).flatMap(([field, figure]) => ('value' in figure && figure.value !== null ? [[field, figure.value]] : [])),
  );
}
