// Related predicates share a family of colours so they read as a group on the graph; a predicate not listed
// takes a stable colour hashed from its name, so listing one is optional. The names are those
// vos.Tools.ModelIngest's relationship extractors emit.
//
// The families:
// - containment and hierarchy: green
// - the material chain: brown and tan
// - mechanical, electrical and plumbing connectivity: magenta and cyan, in high contrast, because it is
//   usually what a question about building services is about
// - openings and boundaries: amber and orange
// - classification: cyan
// - metadata and process: muted slate and lavender
// - coverings: tan
// - clashes: red, as an alert
export const CURATED_PREDICATE_COLORS: Record<string, string> = {
  // Containment and hierarchy: the green family.
  is:             '#a78bfa', // violet, because the type relationship is everywhere and has to stand out
  has:            '#86efac',
  contains:       '#86efac',
  isContainedIn:  '#86efac',
  aggregates:     '#4ade80', // stronger for the parent side
  isAggregatedBy: '#4ade80',
  nests:          '#4ade80',
  hasMember:      '#86efac',

  // The material chain: browns and tans.
  hasMaterial:    '#a8744d',
  hasLayer:       '#c8a47e',
  hasLayerSet:    '#c8a47e',
  hasConstituent: '#c8a47e',
  hasProfile:     '#c8a47e',
  hasProfileSet:  '#c8a47e',

  // Building services and connectivity: magenta and cyan.
  hasPort:         '#f0abfc', // the IfcDistributionPort node's colour
  connectsTo:      '#22d3ee',
  connectsElement: '#22d3ee',
  connectsPath:    '#22d3ee',
  connectsThrough: '#22d3ee',
  services:        '#67e8f9',

  // Openings and boundaries: amber and orange.
  voids:       '#fbbf24',
  fills:       '#fbbf24',
  hasBoundary: '#fb923c',

  // Classification and type information: cyan.
  hasClassification: '#67e8f9',
  typeInfo:          '#67e8f9',

  // Documentation, process and actors: muted.
  hasDocument:   '#a5b4fc',
  hasConstraint: '#a5b4fc',
  hasActor:      '#fda4af',
  hasProcess:    '#bef264',
  referencedIn:  '#94a3b8',

  // Coverings: tan.
  covers: '#c8a47e',

  // Clashes: red, as an alert.
  interferesWith: '#f87171',

  // Generic descriptors: slate.
  node:  '#94a3b8',
  value: '#94a3b8',
};
