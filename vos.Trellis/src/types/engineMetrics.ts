/** GET /api/engines/metrics: counts and an estimate of size, so reading it never works out a range or a reduction. */

interface RangeEngineMetrics {
  RegisteredRanges: number;
  RangesWithBindings: number;
  PropertyDependencyEdges: number;
  StateDependencyEdges: number;
  BindingDependencyEdges: number;
  DependencyEdges: number;
  EstimatedBytes: number;
}

interface DerivedPropertyEngineMetrics {
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
