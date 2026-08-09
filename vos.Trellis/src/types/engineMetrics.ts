/** Shapes served by GET /api/engines/metrics (#5854) — the per-model capacity summary of the two
 *  reactive engines. Counts and a deterministic footprint estimate; reading one never evaluates a
 *  range or a reduction. */

export interface RangeEngineMetrics {
  RegisteredRanges: number;
  RangesWithBindings: number;
  PropertyDependencyEdges: number;
  StateDependencyEdges: number;
  BindingDependencyEdges: number;
  DependencyEdges: number;
  EstimatedBytes: number;
}

export interface RollupEngineMetrics {
  ThingsOwningRollups: number;
  RollupProperties: number;
  MemberEdges: number;
  EstimatedBytes: number;
}

export interface EngineMetricsSummary {
  ModelId: string;
  ModelName: string;
  Ranges: RangeEngineMetrics;
  Rollups: RollupEngineMetrics;
  EstimatedBytesTotal: number;
}
