/**
 * The arithmetic a hand-drawn chart needs: a scale from values to pixels, round tick values, and a
 * domain that fits the data with a little air. Pure, so every chart widget draws with the same
 * answers and none carries its own.
 */

export type Scale = (value: number) => number;

/** A value on `[from, to]` placed on `[start, end]`; the range may run either way. A flat domain
 *  places every value at the middle rather than dividing by nothing. */
export function linearScale([from, to]: [number, number], [start, end]: [number, number]): Scale {
  const span = to - from;
  if (span === 0) return () => (start + end) / 2;
  return (value) => start + ((value - from) / span) * (end - start);
}

/** Round tick values covering the domain, stepped by 1, 2 or 5 times a power of ten so that about
 *  `count` of them fit. The first is at or below the floor, the last at or above the ceiling. */
export function niceTicks(floor: number, ceiling: number, count: number): number[] {
  if (ceiling === floor) return [floor, floor];
  const rough = (ceiling - floor) / count;
  const magnitude = 10 ** Math.floor(Math.log10(rough));
  const residual = rough / magnitude;
  const step = (residual < 1.5 ? 1 : residual < 3 ? 2 : residual < 7 ? 5 : 10) * magnitude;
  const first = Math.floor(floor / step) * step;
  const last = Math.ceil(ceiling / step) * step;
  const ticks: number[] = [];
  for (let tick = first; tick <= last + step / 2; tick += step) ticks.push(roundTo(tick, step));
  return ticks;
}

/** How much air the fitted domain leaves above and below the data. */
const AIR = 0.08;

/** The floor and ceiling a chart draws between: what the spec states, or the data's own extent with
 *  a little air on each side it left open. A flat series is given a span so it still has a scale. */
export function paddedDomain(
  [lowest, highest]: [number, number], floor?: number, ceiling?: number,
): [number, number] {
  const spread = highest - lowest || Math.abs(highest) || 1;
  return [floor ?? lowest - spread * AIR, ceiling ?? highest + spread * AIR];
}

/** Ticks stepped in tenths come out as 0.30000000000000004 otherwise. */
function roundTo(value: number, step: number): number {
  const decimals = Math.max(0, -Math.floor(Math.log10(step)));
  return Number(value.toFixed(decimals));
}
