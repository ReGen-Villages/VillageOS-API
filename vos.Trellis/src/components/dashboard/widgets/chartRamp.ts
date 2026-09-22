/**
 * The sequential ramp a heatmap paints with: one hue, light at the floor and dark at the ceiling, in
 * the steps the chart palette validates. The dark surface takes fewer steps at the deep end, so the
 * darkest cell still stands off the card it is painted on.
 */

const LIGHT_STEPS = [
  '#cde2fb', '#b7d3f6', '#9ec5f4', '#86b6ef', '#6da7ec', '#5598e7', '#3987e5',
  '#2a78d6', '#256abf', '#1c5cab', '#184f95', '#104281', '#0d366b',
];
const DARK_STEPS = LIGHT_STEPS.slice(0, 10);

export type Surface = 'light' | 'dark';

/** The colour a share of the scale takes, as a lowercase hex triplet. */
export function rampColour(share: number, surface: Surface): string {
  const steps = surface === 'dark' ? DARK_STEPS : LIGHT_STEPS;
  const position = Math.max(0, Math.min(1, share)) * (steps.length - 1);
  const below = Math.floor(position);
  const above = Math.min(steps.length - 1, below + 1);
  const between = position - below;
  return mix(steps[below], steps[above], between);
}

function mix(from: string, to: string, share: number): string {
  const channel = (at: number) => {
    const a = parseInt(from.slice(at, at + 2), 16);
    const b = parseInt(to.slice(at, at + 2), 16);
    return Math.round(a + (b - a) * share).toString(16).padStart(2, '0');
  };
  return `#${channel(1)}${channel(3)}${channel(5)}`;
}
