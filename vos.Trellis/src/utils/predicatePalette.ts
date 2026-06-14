// Bug/Feature #5340 — curated edge colors for known predicates.
//
// Resolution priority for an edge color (see resolvePredicateColor in
// ./colors.ts):
//
//   1. User override from GUI_Settings.PredicateColors (Mycelium-stored,
//      per-deployment customization — already wired)
//   2. Curated default from this map (shipped with the GUI; semantic
//      grouping so related predicates read as a family on the graph)
//   3. Hash fallback into PREDICATE_PALETTE (deterministic per name)
//
// Adding a new predicate from the IFC pipeline (or any other source) is a
// one-line entry here. Anything not listed gets a stable hash color, so the
// graph stays readable without a forced edit.

/**
 * Curated default predicate -> hex color. Grouped by semantic role:
 *
 *   containment / hierarchy  → green family
 *   material chain           → brown / tan family
 *   MEP connectivity         → magenta / cyan (high contrast — these are
 *                              usually the question subject in MEP queries)
 *   openings & boundaries    → amber / orange family
 *   classification           → cyan
 *   metadata / process       → muted slate / lavender
 *   coverings                → tan
 *   clashes                  → red (alert!)
 *
 * Names match exactly what the IFC ingest pipeline emits (verified against
 * vos.Tools.IfcIngest/Pipeline/*RelationshipExtractor.cs).
 */
export const CURATED_PREDICATE_COLORS: Record<string, string> = {
  // ── containment / hierarchy (green family) ───────────────────────────
  is:             '#a78bfa', // violet — type relation, ubiquitous, must stand out
  has:            '#86efac', // green-300
  contains:       '#86efac',
  isContainedIn:  '#86efac',
  aggregates:     '#4ade80', // green-400 (more saturated for parent-of)
  isAggregatedBy: '#4ade80',
  nests:          '#4ade80',
  hasMember:      '#86efac',

  // ── material chain (brown / tan family) ──────────────────────────────
  hasMaterial:    '#a8744d', // brown
  hasLayer:       '#c8a47e', // tan
  hasLayerSet:    '#c8a47e',
  hasConstituent: '#c8a47e',
  hasProfile:     '#c8a47e',
  hasProfileSet:  '#c8a47e',

  // ── MEP / connectivity (magenta / cyan family) ───────────────────────
  hasPort:         '#f0abfc', // magenta — matches the IfcDistributionPort node color
  connectsTo:      '#22d3ee', // cyan-400
  connectsElement: '#22d3ee',
  connectsPath:    '#22d3ee',
  connectsThrough: '#22d3ee',
  services:        '#67e8f9', // cyan-300

  // ── openings & boundaries (amber / orange family) ────────────────────
  voids:       '#fbbf24', // amber
  fills:       '#fbbf24',
  hasBoundary: '#fb923c', // orange-400

  // ── classification / type info (cyan) ────────────────────────────────
  hasClassification: '#67e8f9', // cyan-300
  typeInfo:          '#67e8f9',

  // ── documentation / process / actors (muted) ─────────────────────────
  hasDocument:   '#a5b4fc', // indigo-300
  hasConstraint: '#a5b4fc',
  hasActor:      '#fda4af', // rose-300
  hasProcess:    '#bef264', // lime-300
  referencedIn:  '#94a3b8', // slate-400 — soft / metadata-ish

  // ── coverings (tan) ──────────────────────────────────────────────────
  covers: '#c8a47e',

  // ── clashes (red — alert) ────────────────────────────────────────────
  interferesWith: '#f87171', // red-400

  // ── generic descriptors (slate) ──────────────────────────────────────
  node:  '#94a3b8',
  value: '#94a3b8',
};
