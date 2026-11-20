export const ROLE_COLORS = {
  predicate: '#fbbf24',
  type: '#60a5fa',
  noType: '#94a3b8',    // an instance with no "is" relationship
};

// Hashed by type name, so instances of a type share a colour.
export const INSTANCE_PALETTE = [
  '#f87171',
  '#fb923c',
  '#fbbf24',
  '#a3e635',
  '#4ade80',
  '#2dd4bf',
  '#22d3ee',
  '#38bdf8',
  '#818cf8',
  '#a78bfa',
  '#c084fc',
  '#e879f9',
  '#f472b6',
  '#fb7185',
  '#34d399',
  '#facc15',
];

// Paler, for nodes with no position, so they stand apart from the ones on the ground.
export const LOGICAL_PALETTE = [
  '#fca5a5',
  '#fdba74',
  '#fcd34d',
  '#bef264',
  '#86efac',
  '#5eead4',
  '#67e8f9',
  '#7dd3fc',
  '#a5b4fc',
  '#c4b5fd',
  '#d8b4fe',
  '#f0abfc',
  '#f9a8d4',
  '#fda4af',
  '#6ee7b7',
  '#fde047',
];

// Muted, for building elements in the 3D view, hashed by name.
export const ELEMENT_COLORS = [
  '#6d8ea8',
  '#7a9b6d',
  '#a8846d',
  '#8b7da8',
  '#6da89b',
  '#a88b6d',
  '#6d7fa8',
  '#8da86d',
];

export const PREDICATE_PALETTE = [
  '#818cf8',
  '#fb7185',
  '#34d399',
  '#fbbf24',
  '#a78bfa',
  '#22d3ee',
  '#f472b6',
  '#fb923c',
];

/** `hex` is a seven-character colour such as "#f87171"; `t` runs from 0, unchanged, to 1, white. */
export function brightenColor(hex: string, t: number): string {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  const mix = (c: number) => Math.round(c + (255 - c) * t);
  return `#${mix(r).toString(16).padStart(2, '0')}${mix(g).toString(16).padStart(2, '0')}${mix(b).toString(16).padStart(2, '0')}`;
}

/** A colour that is not a hex triplet, such as a name or an `rgb()`, takes light ink, which reads on every
 *  face a model is likely to declare. */
export function inkFor(colour: string): 'light' | 'dark' {
  const channels = hexChannels(colour);
  if (!channels) return 'light';
  const linear = (channel: number) => {
    const fraction = channel / 255;
    return fraction <= 0.03928 ? fraction / 12.92 : ((fraction + 0.055) / 1.055) ** 2.4;
  };
  const [red, green, blue] = channels.map(linear);
  const luminance = 0.2126 * red + 0.7152 * green + 0.0722 * blue;
  return luminance > PALE_FACE_LUMINANCE ? 'dark' : 'light';
}

/** Above this the face is pale enough that white ink washes out. */
const PALE_FACE_LUMINANCE = 0.55;

function hexChannels(colour: string): [number, number, number] | null {
  const hex = colour.trim();
  const match = /^#([0-9a-f]{3}|[0-9a-f]{6})$/i.exec(hex);
  if (!match) return null;
  const digits = match[1].length === 3 ? [...match[1]].map((digit) => digit + digit).join('') : match[1];
  return [0, 2, 4].map((at) => parseInt(digits.slice(at, at + 2), 16)) as [number, number, number];
}

export function hashStringToIndex(s: string, paletteSize: number): number {
  let hash = 0;
  for (let i = 0; i < s.length; i++) {
    hash = ((hash << 5) - hash + s.charCodeAt(i)) | 0;
  }
  return Math.abs(hash) % paletteSize;
}

import { CURATED_PREDICATE_COLORS } from './predicatePalette';

/** The deployment's own colour from GUI_Settings.PredicateColors first, then the chosen default, which groups
 *  related predicates by colour, then a hash of the name, so a predicate nobody coloured still keeps one. */
export function resolvePredicateColor(
  name: string,
  overrides: Record<string, string>,
): string {
  if (overrides[name]) return overrides[name];
  if (CURATED_PREDICATE_COLORS[name]) return CURATED_PREDICATE_COLORS[name];
  return PREDICATE_PALETTE[hashStringToIndex(name, PREDICATE_PALETTE.length)];
}
