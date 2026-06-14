// ── Colour palettes ────────────────────────────────────────────────────
// Shared across graphologyMapper, predicateCluster, and other modules
// that need deterministic colouring.

/** Role-based colors for structural node kinds (predicates, types). */
export const ROLE_COLORS = {
  predicate: '#fbbf24', // amber — predicates / handlers
  type: '#60a5fa',      // blue  — type definitions
  noType: '#94a3b8',    // slate — instances with no "is" relationship
};

/**
 * Vibrant palette for instance nodes.  A type name is hashed into this
 * palette so every instance of the same type shares a colour.  The palette
 * is intentionally large and saturated so buildings, sensors, etc. are
 * visually distinct on the map.
 */
export const INSTANCE_PALETTE = [
  '#f87171', // red-400
  '#fb923c', // orange-400
  '#fbbf24', // amber-400
  '#a3e635', // lime-400
  '#4ade80', // green-400
  '#2dd4bf', // teal-400
  '#22d3ee', // cyan-400
  '#38bdf8', // sky-400
  '#818cf8', // indigo-400
  '#a78bfa', // violet-400
  '#c084fc', // purple-400
  '#e879f9', // fuchsia-400
  '#f472b6', // pink-400
  '#fb7185', // rose-400
  '#34d399', // emerald-400
  '#facc15', // yellow-400
];

/**
 * Softer palette for logical (non-geo) instance nodes.  Uses pastel /
 * desaturated tones so they are clearly distinct from the vibrant physical
 * nodes on the map while still being legible against the dark background.
 */
export const LOGICAL_PALETTE = [
  '#fca5a5', // red-300
  '#fdba74', // orange-300
  '#fcd34d', // amber-300
  '#bef264', // lime-300
  '#86efac', // green-300
  '#5eead4', // teal-300
  '#67e8f9', // cyan-300
  '#7dd3fc', // sky-300
  '#a5b4fc', // indigo-300
  '#c4b5fd', // violet-300
  '#d8b4fe', // purple-300
  '#f0abfc', // fuchsia-300
  '#f9a8d4', // pink-300
  '#fda4af', // rose-300
  '#6ee7b7', // emerald-300
  '#fde047', // yellow-300
];

/** Muted palette for 3D building/IFC element coloring (by name hash). */
export const ELEMENT_COLORS = [
  '#6d8ea8', // steel blue
  '#7a9b6d', // sage
  '#a8846d', // sandstone
  '#8b7da8', // lavender
  '#6da89b', // teal
  '#a88b6d', // copper
  '#6d7fa8', // slate blue
  '#8da86d', // moss
];

export const PREDICATE_PALETTE = [
  '#818cf8', // indigo-400
  '#fb7185', // rose-400
  '#34d399', // emerald-400
  '#fbbf24', // amber-400
  '#a78bfa', // violet-400
  '#22d3ee', // cyan-400
  '#f472b6', // pink-400
  '#fb923c', // orange-400
];

/**
 * Lighten a hex colour by mixing it toward white.
 * @param hex  7-char hex string, e.g. "#f87171"
 * @param t    0 = unchanged, 1 = pure white
 */
export function brightenColor(hex: string, t: number): string {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  const mix = (c: number) => Math.round(c + (255 - c) * t);
  return `#${mix(r).toString(16).padStart(2, '0')}${mix(g).toString(16).padStart(2, '0')}${mix(b).toString(16).padStart(2, '0')}`;
}

/** Deterministic hash of a string into a palette index. */
export function hashStringToIndex(s: string, paletteSize: number): number {
  let hash = 0;
  for (let i = 0; i < s.length; i++) {
    hash = ((hash << 5) - hash + s.charCodeAt(i)) | 0;
  }
  return Math.abs(hash) % paletteSize;
}

import { CURATED_PREDICATE_COLORS } from './predicatePalette';

/**
 * Resolve a predicate's edge color. Three-tier priority chain (Bug #5340):
 *
 *   1. User override from GUI_Settings.PredicateColors (per-deployment
 *      customization — Mycelium-stored, fetched on login)
 *   2. Curated default from CURATED_PREDICATE_COLORS (semantic grouping
 *      so material edges read as a family, MEP connectivity stands out, etc.)
 *   3. Hash fallback into PREDICATE_PALETTE (deterministic per name —
 *      anything not curated still gets a stable color)
 *
 * @param name       Predicate name (e.g. "hasPort", "consumes")
 * @param overrides  Name→hex map from GUI_Settings.PredicateColors
 */
export function resolvePredicateColor(
  name: string,
  overrides: Record<string, string>,
): string {
  if (overrides[name]) return overrides[name];
  if (CURATED_PREDICATE_COLORS[name]) return CURATED_PREDICATE_COLORS[name];
  return PREDICATE_PALETTE[hashStringToIndex(name, PREDICATE_PALETTE.length)];
}
