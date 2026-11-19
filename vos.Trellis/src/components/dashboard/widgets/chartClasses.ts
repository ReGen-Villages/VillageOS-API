import { asNumber, type BindingState, type RangeBand } from '@tendril-ui/tendril';

export type DrawnClass = { label: string; colour: string; from: number | null; to: number | null };

/** The classes with their bounds resolved, where `bounds` holds each class's `from` then `to` in order. */
export function drawnClassesOf(classes: RangeBand[] | undefined, bounds: BindingState[]): DrawnClass[] {
  return (classes ?? []).map((entry, at) => ({
    label: entry.label,
    colour: entry.colour,
    from: entry.from ? asNumber(bounds[at * 2]?.value ?? null) : null,
    to: entry.to ? asNumber(bounds[at * 2 + 1]?.value ?? null) : null,
  }));
}

export function classHolding(classes: DrawnClass[], value: number): DrawnClass | undefined {
  return classes.find((entry) => (entry.from === null || value >= entry.from) && (entry.to === null || value < entry.to));
}
