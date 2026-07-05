// Core domain model types — mirrors Mycelium PascalCase JSON serialization

export interface VosThing {
  Id: string;
  Name: string;
  Properties: Record<string, unknown>;
  InheritedProperties?: Record<string, InheritedPropertySet>;
}

export interface PropertyValue {
  Value: unknown;
  Type?: string;
  IsInherited?: boolean;
  InheritedFrom?: string;
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

export interface ThingsInStateResponse {
  StateName: string;
  Things: Array<{ Id: string; Name: string }>;
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
