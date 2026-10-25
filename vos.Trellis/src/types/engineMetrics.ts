/** Shapes served by GET /api/engines/metrics — the per-model capacity summary of the two
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

export interface DerivedPropertyEngineMetrics {
  ThingsOwningDerivedProperties: number;
  Definitions: number;
  MemberEdges: number;
  EstimatedBytes: number;
}

export interface EngineMetricsSummary {
  ModelId: string;
  ModelName: string;
  Ranges: RangeEngineMetrics;
  DerivedProperties: DerivedPropertyEngineMetrics;
  EstimatedBytesTotal: number;
}
