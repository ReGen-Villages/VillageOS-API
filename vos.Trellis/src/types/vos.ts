// Core domain model types — mirrors Mycelium PascalCase JSON serialization

export interface VosThing {
  Id: string;
  Name: string;
  Properties: Record<string, unknown>;
  /** Stored per-instance overrides of inherited names, keyed by source (the wire field
   *  `InheritedOverrides`). Override-only — NOT the full inherited view; for that read the
   *  server-resolved effective properties (GET /api/things/{id}/properties). */
  InheritedOverrides?: Record<string, InheritedPropertySet>;
}

export interface InheritedPropertySet {
  SourceId: string;
  SourceName: string;
  InheritedAt: string;
  Properties: Record<string, unknown>;
  Inherited?: Record<string, InheritedPropertySet>;
}

export interface VosRelationship {
  Id: string;
  Name: string;
  SubjectId: string;
  PredicateId: string;
  TargetId: string;
  Properties: Record<string, unknown>;
}

export interface VosModel {
  Id?: string;
  Name?: string;
  Things: VosThing[];
  Relationships: VosRelationship[];
}

export interface ModelSummary {
  Id: string;
  Name: string;
}

// Temporal types

export interface PropertyVersion {
  Timestamp: string;
  Value: unknown;
}

export interface PropertyVersionsResponse {
  ObjectId: string;
  PropertyName: string;
  StartTime: string;
  EndTime: string;
  Versions: PropertyVersion[];
}

export interface MutationDto {
  Timestamp: string;
  PropertyName: string;
  OldValue: unknown;
  NewValue: unknown;
}

export interface ThingMutations {
  ObjectId: string;
  ObjectName: string;
  StartTime: string;
  EndTime: string;
  Mutations: MutationDto[];
}

export interface ModelMutations {
  StartTime: string;
  EndTime: string;
  ThingMutations: Record<string, ThingMutations>;
  TotalMutations: number;
}

export interface RelationshipMutations {
  RelationshipId: string;
  RelationshipName: string;
  SubjectId: string;
  PredicateId: string;
  TargetId: string;
  StartTime: string;
  EndTime: string;
  Mutations: MutationDto[];
}

// Ranges and States

export interface PropertyBindingDto {
  PropertyName: string;
  BoundsDescription: string;
  BoundsType: string;
  GuardCriteria?: string;
  IsActive: boolean;
  CurrentValue?: unknown;
  IsInBounds?: boolean;
  DeviationDelta?: number;
  DeviationSeverity?: number;
}

export interface RangeDto {
  Name: string;
  Criteria: string;
  IsInherited: boolean;
  InheritedFromId?: string;
  ActiveBindings: number;
  Bindings: PropertyBindingDto[];
}

export interface InheritedRangeSetDto {
  SourceId: string;
  SourceName: string;
  InheritedAt: string;
  Ranges: RangeDto[];
  Inherited: InheritedRangeSetDto[];
}

export interface ThingRangesResponse {
  ThingId: string;
  ThingName: string;
  OwnRanges: RangeDto[];
  InheritedRanges: InheritedRangeSetDto[];
}

export interface RangeEvaluation {
  RangeName: string;
  IsActive: boolean;
  Criteria: string;
  Error?: string;
}

export interface ThingStates {
  ThingId: string;
  ThingName: string;
  CurrentStates: string[];
  RangeEvaluations: RangeEvaluation[];
  OutOfBoundsCount: number;
}

/** Response of GET /api/things/{id}/states — a single Thing's currently-holding derived states. */
export interface ObjectStatesResponse {
  ObjectId: string;
  ObjectName: string;
  CurrentStates: string[];
  RangeEvaluations: RangeEvaluation[];
  OutOfBoundsCount: number;
}

/** One PropertyValueAsserted/Retracted record from the Commit Log (provenance audit trail). */
export interface PropertyFact {
  kind: 'asserted' | 'retracted';
  sequenceNumber: number;
  committedAt: string;
  author?: string;
  value?: unknown;
}

export interface PropertyFactsResponse {
  entityId: string;
  property: string;
  count: number;
  entries: PropertyFact[];
}

export interface RelationshipRangesResponse {
  RelationshipId: string;
  RelationshipName: string;
  OwnRanges: RangeDto[];
}

export interface RelationshipStates {
  RelationshipId: string;
  RelationshipName: string;
  CurrentStates: string[];
  RangeEvaluations: RangeEvaluation[];
  OutOfBoundsCount: number;
}

// Composite range summary (single-call replacement for 2N+2 individual fetches)

export interface RelationshipRangeSummary {
  RelationshipId: string;
  RelationshipName: string;
  SubjectId: string;
  PredicateId: string;
  TargetId: string;
  SubjectName: string;
  PredicateName: string;
  TargetName: string;
  OwnRanges: RangeDto[];
  CurrentStates: string[];
  RangeEvaluations: RangeEvaluation[];
  OutOfBoundsCount: number;
}

export interface ThingRangeSummary {
  ObjectId: string;
  ObjectName: string;
  OwnRanges: RangeDto[];
  InheritedRanges: InheritedRangeSetDto[];
  CurrentStates: string[];
  RangeEvaluations: RangeEvaluation[];
  OutOfBoundsCount: number;
  Relationships: RelationshipRangeSummary[];
}

export interface EffectiveProperty {
  Value: unknown;
  Type: string;
  IsInherited: boolean;
  InheritedFrom?: string;
}

// Range CRUD request/response

export interface CreateRangeRequest {
  Name: string;
  Criteria: string;
  Property?: string;
  Bounds?: { Min?: number; Max?: number };
}

export interface CriteriaValidationResult {
  IsValid: boolean;
  Error?: string;
}

// State query

/** Response of GET /api/states/{state}/things. The kinds Things `is` are left out unless the
 *  request asks for them, and `Properties` is present only on the entries of a request that named
 *  properties — a name the Thing does not hold is absent from it rather than null. */
export interface ThingsInStateResponse {
  StateName: string;
  Things: Array<{ Id: string; Name: string; Properties?: Record<string, unknown> }>;
}

/** Window the returned state history covers. `Source` is "in-memory" while history comes from the
 *  live engine tracker — it starts at model load and is lost on restart — and becomes
 *  "reconstructed" once served from durable history, over a wider window and with no client change. */
export interface StateHistoryCoverage {
  Source: 'in-memory' | 'reconstructed';
  From: string;
  To: string;
}

/** One change point: the states entered and exited at `At`, the full set after it, and the
 *  property write that caused it. */
export interface StateTransition {
  At: string;
  Entered: string[];
  Exited: string[];
  States: string[];
  TriggeringProperty?: string | null;
  OldValue?: unknown;
  NewValue?: unknown;
}

/** Response of GET /api/things/{id}/state-transitions. */
export interface StateTransitionsResponse {
  ThingId: string;
  ThingName: string;
  Coverage: StateHistoryCoverage;
  Transitions: StateTransition[];
}

/** One interval a Thing held a state; `ExitedAt` is null while it is still in it. */
export interface StateOccurrence {
  EnteredAt: string;
  ExitedAt?: string | null;
}

/** Response of GET /api/things/{id}/states/{stateName}/occurrences. */
export interface StateOccurrencesResponse {
  ThingId: string;
  ThingName: string;
  StateName: string;
  Coverage: StateHistoryCoverage;
  Occurrences: StateOccurrence[];
}

// Property mode configuration

export interface PropertyModeConfig {
  Mode: string;
  RingBufferSize?: number;
  SampleRate?: number;
}

// Temporal snapshot
export interface TemporalSnapshot {
  Timestamp: string;
  Things: Array<{ Id: string; Name: string; Properties: Record<string, unknown> }>;
  Relationships: Array<{ Id: string; SubjectId: string; PredicateId: string; TargetId: string; Properties: Record<string, unknown> }>;
}
