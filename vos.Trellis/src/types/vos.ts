// Core domain model types — mirrors Mycelium PascalCase JSON serialization

/**
 * Which kinds of write a property accepts, as the model declares it: a value a person asserts
 * (`FactOnly`), or a value sampled or fetched from outside (`ObservationOnly`). The platform sends
 * it only where the declaration narrows what the property takes, so a property open to either
 * carries none — and a property nothing declares is silent rather than described as one or the other.
 */
export const DECLARED_WRITE_KINDS = ['FactOnly', 'ObservationOnly'] as const;
export type DeclaredWriteKind = (typeof DECLARED_WRITE_KINDS)[number];

/**
 * Where a value came from: stated by whoever the Thing is about, measured or fetched from outside,
 * assumed by the platform because the archetype supplies it, or unrecorded.
 *
 * Structural, not domain vocabulary — how a value entered the model, in words that hold for any
 * model. What each one *reads as* on a page is the model's to write. Resolved by
 * `valueOrigin` in src/utils/propertyOrigin.ts.
 */
export const ORIGIN_KINDS = ['stated', 'measured', 'assumed', 'unknown'] as const;
export type OriginKind = (typeof ORIGIN_KINDS)[number];

export interface ValueOrigin {
  origin: OriginKind;
  /** The Thing an assumption was inherited from, which is what says so. Null for every other
   *  origin: a value the Thing itself holds is not inherited from anywhere. */
  assumedFrom: string | null;
}

export interface VosThing {
  Id: string;
  Name: string;
  /** Declared when the Thing was created, because nothing can re-derive it: a type and a member are
   *  the same shape, and a type whose members do not exist yet has no `is` edge to give it away. */
  IsArchetype?: boolean;
  Properties: Record<string, unknown>;
  /** The write kind each stored property was declared with, keyed the way `Properties` is. Unwrapping
   *  keeps only what a value *is*; this is what the model says about how it came to be one, and it is
   *  the only record of that a client holds. */
  PropertyWriteKinds?: Record<string, DeclaredWriteKind>;
  /** Stored per-instance overrides of inherited names, keyed by source (the wire field
   *  `InheritedOverrides`). Override-only — NOT the full inherited view; for that read the
   *  server-resolved effective properties (GET /api/things/{id}/properties). */
  InheritedOverrides?: Record<string, InheritedPropertySet>;
  /** How each computed value this Thing OWNS is worked out, keyed the way `Properties` is. A value the
   *  platform derives arrives with its number and a type that says only that it was computed; this is
   *  what says from what. Own definitions only — a member computing its own value carries the value and
   *  not the formula, so read the formula off the archetype by walking `is`, the way an inherited value
   *  is read. */
  RollupProperties?: Record<string, DerivedDefinition>;
}

/** How one computed value is worked out. One form is described and never both: `Expression` for a
 *  formula, or the reduction's fields. `Reads` names what it reads by the name you can look up on the
 *  Thing you already have — never parse `Expression` to recover them, since a second parser in a client
 *  drifts from the one the platform evaluates with. */
export interface DerivedDefinition {
  Expression?: string;
  Function?: string;
  Path?: string;
  RelatedType?: string;
  PropertyPath?: string;
  Scope?: string;
  Reads?: string[];
  /** Which way the result moves as each input rises, where the definition's structure settles it —
   *  each a subset of `Reads`. A name in `Reads` and in neither list has no one direction, and
   *  nothing is offered for it: under a shortfall a wrong direction is worse than none. */
  RisesWith?: string[];
  FallsWith?: string[];
}

export interface InheritedPropertySet {
  SourceId: string;
  SourceName: string;
  InheritedAt: string;
  Properties: Record<string, unknown>;
  /** As on {@link VosThing} — a value stored under an inherited name keeps the write kind the
   *  declaration gave it. */
  PropertyWriteKinds?: Record<string, DeclaredWriteKind>;
  Inherited?: Record<string, InheritedPropertySet>;
}

export interface VosRelationship {
  Id: string;
  /** Present only for an edge given a name of its own when it was created. Every other edge is called
   *  "subject predicate target", composed on each read. Read it through `relationshipLabel`, which
   *  composes that form from the endpoints when this is absent. */
  Name?: string;
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

/** A reduction of one type's instances into fixed time buckets across the trailing window. The
 *  window must be a whole number of buckets, and a container is only accepted with the predicate its
 *  containment is written with. */
export interface TemporalAggregateQuery {
  function: 'Min' | 'Max' | 'Sum' | 'Average' | 'Count';
  memberType: string;
  timestampProperty: string;
  measureProperty?: string;
  windowSeconds: number;
  bucketSeconds: number;
  within?: string;
  withinPredicate?: string;
}

export interface TemporalAggregateResponse {
  /** The reduced value of each bucket, oldest first. */
  Buckets: number[];
  FirstBucketStart: string;
  BucketSeconds: number;
  /** Members carrying no readable instant or measure. A metric reading zero because nobody stamped
   *  it looks exactly like one reading zero because nothing happened. */
  UnusableMembers: number;
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

/** What a range's criteria compare a property of the judged Thing against, read by the platform from
 *  the parsed criteria rather than its text. Empty wherever the criteria name no threshold, which is
 *  more cases than it looks — a presence test, a pattern, a negated comparison, and two properties
 *  compared with each other all report nothing rather than a number a reader would take for one. */
export interface CriteriaComparisonDto {
  PropertyName: string;
  /** The operator as the criteria language writes it, e.g. ">=". */
  Operator: string;
  Value: unknown;
}

export interface RangeDto {
  Name: string;
  Criteria: string;
  IsInherited: boolean;
  InheritedFromId?: string;
  ActiveBindings: number;
  Bindings: PropertyBindingDto[];
  Comparisons: CriteriaComparisonDto[];
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
