/**
 * Generic, model-agnostic dashboard contract.
 *
 * Trellis ships the *widgets* and a *binding resolver*; the model supplies the
 * mapping. A model declares one or more Things of archetype `Dashboard`, each
 * carrying a `spec` property (JSON) that conforms to {@link DashboardSpec}.
 *
 * Nothing in this file names a domain (no domain-specific nouns at all).
 * Every domain word lives in the model's spec — see the discovery + resolver in
 * `src/api/dashboardApi.ts`.
 */
import type { HistoryFold, HistoryFunction, OriginKind } from './vos';

/** The archetype a model-resident dashboard configuration Thing must be `is`-linked to. */
export const DASHBOARD_ARCHETYPE = 'Dashboard';
/** The Thing property holding the JSON-encoded {@link DashboardSpec}. */
export const DASHBOARD_SPECIFICATION_PROPERTY = 'spec';
/** The spec's reference to "the compare entity currently selected in the scope switcher", which
 *  inside a computed column is the row's own Thing. */
export const SCOPE_REFERENCE = '$scope';
/** The relationship saying what a Thing is. Every reader here follows it to resolve a type's members and
 *  the values they inherit, so both the walk and the subscription that has to carry it name the
 *  same predicate. */
export const IS_PREDICATE = 'is';
/** The property on a page's scope entity that says which clock offset its calendar is read in, which
 *  a `history` binding hands the platform so a fold by day or month is the entity's own day or month
 *  rather than the server's. The one property name this file states, like the spec's own. */
export const UTC_OFFSET_PROPERTY = 'utcOffsetSeconds';

export type NumberFormat =
  | 'integer'
  | 'decimal1'
  | 'decimal2'
  | 'percent'   // value is 0..1 → "81%"
  | 'percent1'  // value is 0..1 → "81.3%"
  | 'pct100'    // value is 0..100 → "81%"
  | 'hours'     // "2.8 h"
  | 'compact'   // 12_400 → "12.4k"
  | 'money';

/** A step of a history reduction as the spec writes it. A parameter — a percentile, a band's bound, a
 *  threshold — is a number, or a binding onto the model's own value (a setpoint the study declares,
 *  a bound a class Thing carries) resolved to the number before the platform is asked. */
export type BoundNumber = number | Binding;
export interface HistoryStepBinding {
  fold: HistoryFold;
  function: HistoryFunction;
  percentile?: BoundNumber;
  from?: BoundNumber;
  to?: BoundNumber;
  threshold?: BoundNumber;
}

/**
 * A Binding is *how a widget slot gets its number/rows*. The `kind` set is
 * generic; the values (`state: 'active'`, `archetype: 'Device'`, property names)
 * are model-specific and come from the spec.
 *
 * The special thing reference `$scope` resolves to the compare-entity currently
 * selected in the page's scope switcher, or is averaged across all compare
 * entities when "All" is selected.
 */
export type Binding =
  | { kind: 'const'; value: number }
  /** Count of Things currently in a derived State, asked of the platform as a number so a figure of
   *  four hundred costs what a figure of four costs. `archetype` narrows the count to Things of that
   *  archetype; `excludeState` drops Things also in that state, so a funnel stage counts only Things
   *  that reached it and no further. */
  | { kind: 'stateCount'; state: string; scope?: ScopeReference; archetype?: string; excludeState?: string }
  /** Rows of Things currently in a State, enriched with their properties for a table.
   *  `excludeState` drops Things also in that state — for a funnel stage, set it to the next
   *  stage's state so the list shows only Things that reached this stage and no further. */
  | {
      kind: 'stateList';
      state: string;
      excludeState?: string;
      scope?: ScopeReference;
      limit?: number;
      archetype?: string;
      /** The stored properties each row carries, which the platform sends beside the id so a row
       *  arrives as it is drawn. A row's `id` and `name` always arrive; naming nothing here asks
       *  for nothing more, and a table drawing a column the binding does not name shows it empty.
       *
       *  A name the Thing does not hold is absent from the row rather than present and empty, so
       *  "no value" reads differently from "the value is nothing". An inherited value reaches the
       *  row like an owned one. A name the Thing holds by two inheritance paths is refused rather
       *  than guessed at, and the whole read fails saying which Thing and which name. */
      properties?: string[];
      computed?: ComputedColumn[];
    }
  /** Rows of every Thing of an archetype, whatever state each is in — the roster a `stateList`
   *  cannot express, since a Thing in no derived state appears in no state's list. Read from the
   *  client-side model index, so instances only: a sub-archetype is descended into, never listed.
   *  Rows are ordered by name, which is what makes a `limit`ed list the same list every time. */
  | {
      kind: 'thingList';
      archetype: string;
      scope?: ScopeReference;
      limit?: number;
      computed?: ComputedColumn[];
      /** Keep only the rows the platform lists for this derived state, asked once for the roster.
       *  Unlike `stateList`, the rows are still the roster's own, so a table narrowed to a state
       *  carries every value the console holds for them. */
      inState?: string;
      /** Keep only the rows whose properties satisfy every comparison — the same comparisons an
       *  `aggregate` takes. */
      where?: PropertyFilter[];
    }
  /** Aggregate over Things of an archetype held in the model store (client-side). */
  | {
      kind: 'aggregate';
      archetype: string;
      op: 'count' | 'sum' | 'avg' | 'min' | 'max';
      property?: string;
      where?: PropertyFilter[];
      scope?: ScopeReference;
    }
  /** A single property of a named/id'd Thing, or of the selected scope entity (`$scope`). */
  | { kind: 'property'; thing: string; property: string }
  /** What a Thing's relationships say rather than what its own properties store: follow `via` from the
   *  starting Thing and read the name of what that reaches, or the named `property` of it. The
   *  archetype a Thing `is`, the location it is `at`, the zone it `operates_in` — each is one
   *  step; a value two relationships away is two. `thing` names the starting Thing and defaults to the
   *  selected scope entity (`$scope`), which inside a computed column is the row's own Thing.
   *  A lone numeric value resolves as a number so it can feed a numeric column or a KPI;
   *  several matches resolve as their names in alphabetical order, joined with ", "; none
   *  resolves to null. */
  | { kind: 'related'; via: RelationStep[]; thing?: string; property?: string }
  /** The derived state a Thing currently holds, as a word for a status cell. `states` lists the
   *  candidates in priority order and the first one the Thing holds wins — derived states nest, so
   *  a Thing can hold several at once and a single-valued cell needs the model to say which state
   *  answers the question. Holding none of them resolves to null.
   *  Asks each listed state who is in it rather than asking each Thing which states it holds:
   *  the rows of one table share those answers, so a status column over a roster costs one
   *  request per listed state per refresh, not one request per row. */
  | { kind: 'stateOf'; states: string[]; thing?: string }
  /** Which of the listed verdicts a Thing holds, each carrying the target its range judged against.
   *  Resolves to one row per verdict held, with keys `state`, `reads`, `property`, `operator`,
   *  `target` and `value`.
   *
   *  Unlike `stateOf`, every candidate the Thing holds is reported rather than the first: ranges are
   *  independent criteria and several can hold at once, and a single-valued answer would hide that.
   *
   *  `property`, `operator` and `target` come from the range's own comparison, so the target named is
   *  the one the model tests and moving it in the model moves what the view says. A range that
   *  compares nothing — the criteria for a balance nobody assessed — reports them as null, which is
   *  how a withheld verdict stays distinct from a failed one. `value` is the Thing's own value for
   *  the property that comparison names, so the figure shown is the figure that was judged.
   *
   *  `via` walks from `thing` to what is actually judged, the way `related` does. A page can be
   *  scoped to one Thing only, and the Thing a view is about is not always the Thing the ranges
   *  hang off — a page about one Thing reads its verdicts off whatever Thing was judged about it.
   *  A walk reaching several judged Things reports every one, in name order.
   *
   *  Costs one range read per judged Thing per refresh, shared across every verdict binding on the
   *  page. The state reads are shared too, so a walk reaching several costs no extra ones. */
  | { kind: 'verdict'; states: VerdictCandidate[]; thing?: string; via?: RelationStep[] }
  /** Where one input came from, so an estimate and a measurement never look alike. Resolves to one
   *  row per Thing reached, with keys `origin`, `reads`, `source` and `resolvedAt`.
   *
   *  `origin` is read from what the model declares about the property, never from what the property
   *  is called: a value the Thing holds is `stated` or `measured` according to the kind of write its
   *  declaration accepts, a value only its archetype carries is `assumed`, and a value the model
   *  declares nothing about is `unknown`. A renamed property answers the same way, and silence is
   *  never reported as a measurement.
   *
   *  `reads` is the wording the spec gives that origin, and is the only wording there is — an origin
   *  the spec leaves unworded says nothing rather than something Trellis made up. `source` names
   *  what says so and `resolvedAt` when it last did: for an assumption, the archetype the value came
   *  from; otherwise what the `source` walk reaches from the Thing holding the value, which is how a
   *  fetched figure names its data source and a generated boundary says it was generated.
   *
   *  Costs no request: every answer is already in the loaded model. */
  | {
      kind: 'origin';
      property: string;
      thing?: string;
      via?: RelationStep[];
      reads: OriginWording;
      source?: OriginSource;
    }
  /** How a computed figure was worked out: the formula the model holds for it, and each input it
   *  reads. Resolves to one row per input, carrying `term` and — where the model computes the figure
   *  from a formula — that `formula` and the input's `value` on the Thing computing it.
   *
   *  Where the model reduces over members instead, the row carries a null `formula` and a
   *  `memberArchetype` rather than a value: the input is held by each member, so the Thing computing
   *  the figure has none, and a property of that name on it is some other property.
   *
   *  The inputs come from the model, never from taking `formula` apart: a client that parsed the
   *  criteria grammar would be a second parser drifting from the one the platform evaluates with. A
   *  formula's terms reached through a path are not among them, because a page cannot resolve one
   *  against the Thing it is showing.
   *
   *  A property the model does not derive resolves to `[]`, so a figure a service asserts shows no
   *  working rather than working Trellis invented. `via` walks to the Thing computing it, the way
   *  `related` and `verdict` do, and a walk reaching several returns each one's rows in turn.
   *
   *  Costs no request: the definition arrives with the Thing and its inputs are already resolved. */
  | { kind: 'working'; property: string; thing?: string; via?: RelationStep[] }
  /** One binding divided by another — a rate the aggregate ops cannot express, because a ratio of
   *  sums is not a sum of ratios. Resolves to null when the denominator is zero or non-numeric. */
  | { kind: 'ratio'; numerator: Binding; denominator: Binding }
  /** One row per compared Thing, carrying the listed numeric properties (leaderboard source). */
  | { kind: 'compareEntities'; properties: string[]; computed?: ComputedColumn[] }
  /** A reduction of an archetype's instances into fixed time buckets across the trailing window,
   *  answered by the platform (POST /api/temporal/aggregate). `happenedAt` names the property each
   *  member carries its event instant on, and `property` the value reduced — absent for a count,
   *  which reduces the members themselves. Buckets arrive oldest first.
   *
   *  **One bucket is a scalar.** A window as wide as its bucket is the trailing-window figure a tile
   *  shows, so a tile and the trace above it are one question asked at two granularities and cannot
   *  disagree.
   *
   *  `buckets` is how many points the line holds and `bucketsPerPoint` how many buckets each point
   *  covers. Points step one bucket on, so they overlap by the rest — which is how a line plots a
   *  figure covering an hour at every quarter hour, a shape the platform's own grid cannot take. A
   *  point is its buckets folded together, and adding needs no division, so a summed or counted
   *  point keeps the unit the tile shows.
   *
   *  An average is refused when a point covers several buckets: the average of the buckets is not
   *  the average of what went into them unless every bucket holds the same number of members, and
   *  nothing here knows that.
   *
   *  An outgoing `scope` narrows to the selected compare entity. The endpoint walks outward from a
   *  container only, so an inbound scope is refused rather than answered as though it had been
   *  applied. */
  | {
      kind: 'timeseries';
      archetype: string;
      happenedAt: string;
      property?: string;
      op: 'count' | 'sum' | 'avg' | 'min' | 'max';
      bucketSeconds: number;
      buckets: number;
      /** Omitted means one, which is the platform's own grid. */
      bucketsPerPoint?: number;
      scope?: ScopeReference;
    }
  /** The newest point of a series, as the number a tile shows. The tile and the line beneath it then
   *  ask the platform one question — each answer costs a walk over every instance of the archetype —
   *  and the figure is a point of that line rather than a second reading that can drift from it. */
  | { kind: 'latest'; series: Extract<Binding, { kind: 'timeseries' }> }
  /** Delegate to a model-side service via POST /api/endpoints/{subdomain}. The escape
   *  hatch for model-specific aggregation. `select` is a dot-path into the JSON reply, and a
   *  `$scope` anywhere in `body` is replaced with the selected compare-entity id (null for "All"),
   *  so the service can answer for the same entity the rest of the page is showing. */
  | { kind: 'service'; endpoint: string; body?: unknown; select?: string }
  /** One property's observation history on the page's scope entity, reduced by the platform
   *  (POST /api/temporal/reduce) through `steps` in order: the first reads the samples, each later one
   *  the previous step's groups. Resolves to one row per group, `{ key, value }`, in the platform's
   *  order — a fold by month of year answers keys `1`–`12`, a composite fold `H,D`, `all` one row.
   *  The offset the calendar is folded in is read off the scope entity's {@link UTC_OFFSET_PROPERTY}.
   *  Resolves to nothing where no scope entity is selected — a series is one Thing's — and where the
   *  platform refuses the question, which it does past ten thousand groups.
   *
   *  Costs one request per distinct question per refresh: the same question from several widgets is
   *  asked once. The platform walks the property's retained samples once per question. */
  | { kind: 'history'; property: string; windowSeconds: number; steps: HistoryStepBinding[] };

/**
 * A column of a row-producing binding (`thingList`, `stateList`, `compareEntities`) derived per
 * row rather than read from a stored property. The binding resolves once per row with that row's
 * Thing as the scope, so `$scope` in it means "this row's Thing" — which is how a column holds
 * a live aggregate over the Thing's members (`aggregate`, `ratio`), what its relationships reach
 * (`related`), or the condition the platform derives for it (`stateOf`).
 *
 * The value lands on the row as it resolves: a number stays a number, text stays text. A binding
 * that resolves to a table or a series has nothing a cell can show and lands empty.
 *
 * Cost: the rows of one resolution share their state reads, so `stateOf`, `stateCount` and
 * `stateList` columns cost one request per state name however many rows there are. A `timeseries`
 * or `service` column has no such sharing and costs one request per row on every refresh.
 */
export interface ComputedColumn {
  /** Row key the column lands on — what a LeaderMetric/TableColumn references. */
  key: string;
  value: Binding;
}

/**
 * One step of a binding's path — `related`'s, or `verdict`'s walk to the Thing the ranges judge:
 * which relationship to follow from the Things reached so far, and which of the Things it reaches to keep.
 * Unlike {@link RelationSpec}, which describes how to render a related Thing on a detail card, a
 * step only narrows a walk down to the neighbour the binding means.
 */
export interface RelationStep {
  predicate: string;
  /** 'out': the current Thing is the subject, follow to the targets. 'in': the reverse. Default 'out'. */
  direction?: 'out' | 'in';
  /** Keep only reached Things of this archetype — the filter for a predicate that reaches several. */
  archetype?: string;
  /** Keep only reached Things currently in this derived state. */
  inState?: string;
  /** Drop reached Things currently in this derived state — how a walk skips the work already
   *  finished and keeps only what is still open. */
  notInState?: string;
}

/** One column of a table a person composed from a kind's own declarations. */
export type ComposedColumn =
  /** A property the row carries, read straight off it. */
  | { source: 'property'; name: string; numeric?: boolean }
  /** What a path of links reaches from the row — the Thing's name, or a property of it. */
  | { source: 'path'; steps: RelationStep[]; property?: string; label: string }
  /** The first of these states the row holds. */
  | { source: 'state'; states: string[] };

/** What a composed table was made from: a kind of Thing, its columns, a filter and a sort. */
export interface Composition {
  kind: string;
  columns: ComposedColumn[];
  /** Keep only the rows holding this derived state. Live only: no read answers a state at an
   *  instant, so a composition carrying a moment carries no state. */
  inState?: string;
  /** Read the rows as the model stood at this instant, rather than as it stands. */
  moment?: string;
  /** Keep only the rows whose properties satisfy every comparison. */
  where?: PropertyFilter[];
  sortKey?: string;
  sortDir?: 'asc' | 'desc';
}

/**
 * A derived state a `verdict` binding asks about, with how it reads in words.
 *
 * The wording is the model's, never Trellis's — a client that supplied "falls short of" would be
 * naming a domain it must stay out of, and would say it in one language for every model. `{value}`
 * and `{target}` are substituted with the judged figure and the target the range tested it against,
 * formatted by the row that renders them; a placeholder the verdict has no figure for is dropped
 * along with the space beside it, which is how the same wording serves an unassessed balance.
 */
export interface VerdictCandidate {
  state: string;
  reads: string;
  /** Marks this state as one that offers what would move the judged figure, and the wording each
   *  offer reads as — `{term}` stands for the input's own name, which is never translated. Which
   *  inputs and which way come from the model: the judged figure's derived definition declares what
   *  it rises and falls with, and the held state's own comparison says which way the result must
   *  move to leave it. The author marks *where* levers appear; the arithmetic decides what. */
  levers?: { raise: string; lower: string };
}

/**
 * How each origin reads, in the model's own words — the same contract {@link VerdictCandidate}
 * keeps. Trellis knows that a value was stated, measured, assumed or unrecorded; it has no wording
 * for any of them, because the words belong to a domain it must stay out of and would be written in
 * one language for every model.
 *
 * `{source}` and `{resolvedAt}` are substituted with what says so and when it last did, and a
 * placeholder with nothing to put in it is dropped along with the space beside it — so one wording
 * serves a figure whose source is named and one whose is not. An origin left unworded draws no
 * line: a placeholder would read as an answer.
 */
export type OriginWording = Partial<Record<OriginKind, string>>;

/** Where to find what says a value is so: the path from the Thing holding the value to the Thing
 *  that produced it, and which property of that Thing records when it was last resolved. */
export interface OriginSource {
  via: RelationStep[];
  resolvedAt?: string;
}

export interface PropertyFilter {
  property: string;
  op: '=' | '!=' | '>' | '>=' | '<' | '<=' | 'in';
  value: unknown;
}

/** Narrows a count/aggregate to Things related to the selected scope entity. */
export interface ScopeReference {
  /** Predicate name linking the scope entity to the counted Things. */
  viaPredicate: string;
  /** 'out' = scope is the Subject, 'in' = scope is the Target. Default 'out'. */
  direction?: 'out' | 'in';
}

export interface KpiWidget {
  type: 'kpi';
  title: string;
  value: Binding;
  format?: NumberFormat;
  unit?: string;
  target?: number;
  targetLabel?: string;
  /** Which direction is "good" for the delta + status colouring. Default 'up-good'. `neither-good`
   *  is for a delta that is a discrepancy rather than a trend — a measured area against the one
   *  somebody stated is worth noticing whichever way it went. */
  direction?: 'up-good' | 'down-good' | 'neither-good';
  /** Resolves to a signed change vs. the prior period, or to the discrepancy `neither-good`
   *  describes. */
  delta?: Binding;
  /** Resolves to number[] for the trend sparkline. */
  spark?: Binding;
  /** Resolves to the number the sparkline is read against — drawn as a dashed reference line. */
  sparkBaseline?: Binding;
  sparkBaselineLabel?: string;
  footnote?: string;
  /** Where the figure came from, drawn under it — an `origin` binding. A figure a reader cannot
   *  place is a figure they have to trust, so a tile showing a submitted number and a tile showing
   *  a fetched one say which they are. */
  origin?: Binding;
}

export interface FunnelStage {
  label: string;
  sublabel?: string;
  color?: string;
  count: Binding;
  /** Rows to show when this stage is clicked (drill-down). */
  drill?: Binding;
}

export interface FunnelWidget {
  type: 'funnel';
  title?: string;
  hint?: string;
  stages: FunnelStage[];
  drillColumns?: TableColumn[];
  /** Show a search box that filters the drill rows within a selected stage, or —
   *  with no stage selected — searches across every stage's rows at once. */
  searchable?: boolean;
  /** Row keys the search matches against. Default: every string-valued cell. */
  searchKeys?: string[];
  /** What this funnel's rows are called, in the model's own vocabulary. Names them in the
   *  search placeholder and the cross-stage results heading. Trellis never supplies this
   *  wording itself; absent, it falls back to the generic "rows". */
  searchNoun?: string;
  /** Clicking a drilled/searched row opens a detail window for that row's Thing. */
  rowDetail?: boolean;
}

export interface BulletRow {
  label: string;
  value: Binding;
  /** Track ceiling in the row's own units; the bar, target marker and band are drawn as a
   *  fraction of it. Default 1, so a 0..1 value fills the track — the utilization case. Set it
   *  (e.g. 200 for a percentage that can run past 100) so a value beyond 1 isn't pinned to the edge. */
  max?: number;
  /** Target marker, in the row's own units (0..{@link max}). */
  target?: number;
  /** Healthy band [lo, hi], in the row's own units (0..{@link max}). */
  band?: [number, number];
  /** Which way is "good". 'down-good' (default) keeps a value at or below target healthy, over the
   *  band critical — utilization. 'up-good' inverts it: at or above target is healthy, below the band
   *  is critical — a self-sufficiency / more-is-better metric. */
  direction?: 'up-good' | 'down-good';
  format?: NumberFormat;
}

export interface BulletWidget {
  type: 'bullet';
  title?: string;
  rows: BulletRow[];
}

export interface TableColumn {
  key: string;
  label: string;
  numeric?: boolean;
  format?: NumberFormat;
  /** 'badge' renders a status pill; 'agebar' renders value + inline severity bar; default text. */
  render?: 'text' | 'badge' | 'agebar' | 'id';
}

export interface TableWidget {
  type: 'table';
  title?: string;
  hint?: string;
  columns: TableColumn[];
  rows: Binding;
  minWidth?: number;
  /** Column key to sort by initially. */
  sortKey?: string;
  sortDir?: 'asc' | 'desc';
  /** Cap the table body at this many rows; further rows scroll vertically under the pinned header.
   *  Only the rows inside that window reach the document, so the cap is also what lets a roster
   *  binding drop its `limit` — sorting and searching still run over every row it returned. */
  visibleRows?: number;
  /** Show a search box above the table that filters its rows. */
  searchable?: boolean;
  /** Row keys the search matches against. Default: every string-valued cell. */
  searchKeys?: string[];
  /** Clicking a row opens a detail window for that row's Thing. */
  rowDetail?: boolean;
}

export interface GanttWidget {
  type: 'gantt';
  title?: string;
  hint?: string;
  /** Resolves to GanttRow[]: { row: string, bars: { label?, start, end, color? }[] }, start/end in 0..1. */
  rows: Binding;
  ticks?: string[];
  /** 0..1 position of the "now" marker. */
  now?: number;
}

export interface LeaderMetric {
  key: string;
  label: string;
  format?: NumberFormat;
  direction?: 'up-good' | 'down-good';
  /** Contribution to the weighted score (0..1). Metrics without a weight don't score. */
  weight?: number;
  /** The value that scores full marks. Left out, it is 100 for an `up-good` metric and 0 for a
   *  `down-good` one. */
  best?: number;
  /** The value that scores nothing. Left out, it is 0 for an `up-good` metric and 100 for a
   *  `down-good` one. A metric stating both bounds scores the same under either direction. */
  worst?: number;
}

export interface LeaderboardWidget {
  type: 'leaderboard';
  title?: string;
  hint?: string;
  entities: Binding;
  metrics: LeaderMetric[];
  labelKey?: string;
  sublabelKey?: string;
}

export interface ExceptionBucket {
  label: string;
  value: Binding;
  severity: 'good' | 'warn' | 'crit';
}

export interface ExceptionWidget {
  type: 'exceptionBar';
  title?: string;
  hint?: string;
  buckets: ExceptionBucket[];
  note?: string;
}

/** One judged quantity read as a sentence: what was measured, what it was judged against, and the
 *  verdict in the model's own words. A row shows every verdict its binding reports. */
export interface VerdictRow {
  label: string;
  verdicts: Binding;
  /** Applied to both the judged figure and the target, so a sentence cannot show them in
   *  different units. */
  format?: NumberFormat;
  unit?: string;
}

export interface VerdictWidget {
  type: 'verdict';
  title?: string;
  hint?: string;
  rows: VerdictRow[];
}

/** One figure whose working is shown: what it is called here, the figure itself, and the inputs it was
 *  worked out from. */
export interface WorkingRow {
  label: string;
  /** Resolves to the figure. Usually a `property` or `related` binding onto the same value `working`
   *  describes, so the result shown is the result the inputs were combined into. */
  value: Binding;
  /** A `working` binding: the formula and each input with its current value. */
  working: Binding;
  format?: NumberFormat;
  unit?: string;
}

/** How each figure was worked out, under the figure itself. A row whose working resolves to nothing —
 *  a figure a service asserts rather than one the model derives — shows its value and no working,
 *  because the client has no account of its own to put there. */
export interface WorkingWidget {
  type: 'working';
  title?: string;
  hint?: string;
  rows: WorkingRow[];
}

/** The seven statistics a range bar stacks for one period, each a binding resolving to groups — one
 *  per month for the monthly bars, one for the whole window for the annual bar. Named as a climate
 *  summary names them: the extremes ever recorded, the design values (a high percentile and a low
 *  one), the average of the daily highs and lows, and the mean. */
export interface RangeSeries {
  recordedHigh: Binding;
  designHigh: Binding;
  averageHigh: Binding;
  mean: Binding;
  averageLow: Binding;
  designLow: Binding;
  recordedLow: Binding;
}

/** A band drawn behind a chart between two bounds the model states — a comfort zone, a danger limit.
 *  A bound left unbound runs to the chart's edge, which is how a limit with no upper end is drawn.
 *  The colour is the spec's: a band means what the model says it means, and the widget colours nothing
 *  of its own. */
export interface RangeBand {
  label: string;
  from?: Binding;
  to?: Binding;
  colour: string;
}

/** Twelve stacked range bars, one a month, against bands the model declares, with the same statistics
 *  over the whole window as one bar beside them. Each bar stacks design low to average low, average
 *  low to mean, mean to average high and average high to design high, draws the mean as a line across
 *  it, and the recorded low and high as open circles. `floor` and `ceiling` fix the axis; absent, it
 *  fits the data and the bands. The window label is read off the bindings. */
export interface RangeBarWidget {
  type: 'rangeBar';
  title?: string;
  hint?: string;
  months: RangeSeries;
  annual?: RangeSeries;
  bands?: RangeBand[];
  format?: NumberFormat;
  unit?: string;
  floor?: number;
  ceiling?: number;
}

/** One line of a series chart: what it is called in the legend, and the binding whose groups it
 *  joins — a `history` binding folded by a calendar period, so the keys order along the axis. */
export interface LineSeriesEntry {
  label: string;
  value: Binding;
}

/** Several series over one calendar axis, one line with a point per group each, on one scale — the
 *  monthly means of the daily high, mean and low across the window. Series take the categorical
 *  palette in the order listed; the legend names them. A series the platform answered nothing for is
 *  left out. `floor` and `ceiling` fix the axis; absent, it fits the data. */
export interface LineSeriesWidget {
  type: 'lineSeries';
  title?: string;
  hint?: string;
  series: LineSeriesEntry[];
  format?: NumberFormat;
  unit?: string;
  floor?: number;
  ceiling?: number;
}

/** Where the chart stands on Earth and in what clock, so it can draw when the sun rises and sets:
 *  each a binding onto the model's own figures. The offset is the one the platform folded the hours
 *  in; left unbound, the curves are drawn in universal time as the platform folds by default. */
export interface SunPosition {
  latitude: Binding;
  longitude: Binding;
  utcOffsetSeconds?: Binding;
}

/** Every hour of every day of the year as one cell coloured by value — a `history` binding folded
 *  by `hourOfDay,dayOfYear` — painted on a canvas because the grid is thousands of cells, with the
 *  sunrise and sunset curves over it where the spec binds the coordinates. The ramp is one hue,
 *  light at the floor and dark at the ceiling, with a scale legend; `floor` and `ceiling` fix it,
 *  absent it fits the data. */
export interface HeatmapWidget {
  type: 'heatmap';
  title?: string;
  hint?: string;
  value: Binding;
  sun?: SunPosition;
  format?: NumberFormat;
  unit?: string;
  floor?: number;
  ceiling?: number;
}

/** One class of a stacked share chart: what it is called, the colour the model gives it, and the
 *  binding whose groups are its share of each month — a `history` binding folded by `monthOfYear`
 *  with `ShareWithin` between the class's bounds, answering a fraction. */
export interface StackedSharesClass {
  label: string;
  /** The colour as written, or bound to the class Thing's own — so the model colours its classes. */
  colour: string | Binding;
  share: Binding;
}

/** Twelve bars, one a month, each stacked from the classes' shares in the order listed, the first at
 *  the bottom — the thermal-stress distribution across the year. The shares are the model's answers
 *  and the colours the model's; the widget scales nothing to a hundred, so classes that do not sum
 *  to one draw a bar that does not reach the top. A class the platform answered nothing for is left
 *  out, and so is a month no class was answered for. */
export interface StackedSharesWidget {
  type: 'stackedShares';
  title?: string;
  hint?: string;
  classes: StackedSharesClass[];
}

/** One direction of a diverging bar: what it is called, the binding whose groups are its monthly
 *  figures — a `history` binding folded by `monthOfYear` — and the threshold the figures were counted
 *  against, bound to the model's own value so the legend names the number the model holds. */
export interface DivergingBarSide {
  label: string;
  value: Binding;
  threshold?: Binding;
}

/** Twelve months, each one bar rising above a line and one falling below it, on one scale — the
 *  cooling and heating degree days. The up side takes the warm tone, the down side the cool one. A
 *  month neither side was answered for is left out. */
export interface DivergingBarWidget {
  type: 'divergingBar';
  title?: string;
  hint?: string;
  up: DivergingBarSide;
  down: DivergingBarSide;
  format?: NumberFormat;
  unit?: string;
}

/** One series of a small-multiples panel: what it is called, the binding whose groups are its figure
 *  per month and hour — a `history` binding folded by `monthOfYear,hourOfDay` — and how it is written. */
export interface SmallMultiplesSeries {
  label: string;
  value: Binding;
  unit?: string;
  format?: NumberFormat;
}

/** Twelve monthly panels, each a bar an hour on one scale and a line through the hours on another —
 *  the humidity and the temperature through the day — with a band the model states drawn behind the
 *  line on the line's scale. A month neither series was answered for is left out. */
export interface SmallMultiplesWidget {
  type: 'smallMultiples';
  title?: string;
  hint?: string;
  bars: SmallMultiplesSeries;
  line: SmallMultiplesSeries;
  band?: RangeBand;
}

/** A value a person supplies before pressing — typed, or chosen by name from the Things a binding
 *  lists. Sent to the endpoint under `key`: as a number where `kind` is 'number', as the names
 *  chosen where it is 'multichoice', as text otherwise, and not at all where an optional value was
 *  left empty, because an endpoint reading "" as an answer would be answering a question nobody
 *  asked. */
export interface AskedValue {
  key: string;
  label: string;
  /** How it is entered. 'choice' offers the rows `options` resolves to, by name, and takes one of
   *  them; 'multichoice' offers the same rows and takes as many as are chosen; 'secret' is typed
   *  masked, for a password. Default 'text'. */
  kind?: 'text' | 'number' | 'datetime' | 'secret' | 'choice' | 'multichoice';
  options?: Binding;
  /** What is drawn beside each name in the roster. A field chosen from a roster draws no columns,
   *  so this is what says which values its rows carry, the way a table's columns do. */
  shows?: string[];
  optional?: boolean;
}

/** One button on an action widget. A choice names either the Thing the act is about besides the
 *  row — a reason, a verdict, a disposition the model declares, sent as `reason` and linked from
 *  what was minted — or the act itself, by the name the endpoint accepts it under, sent as `view`. */
export interface ActionChoice {
  label: string;
  target?: string;
  viaPredicate?: string;
  act?: string;
}

/** What pressing a choice writes, and no service named anywhere in it. Which service wakes is the
 *  model's to decide from the relationship the endpoint lays down — a spec naming a handler would move that
 *  decision into the spec. */
export interface ActionRecords {
  /** The endpoint that accepts the act, by the name `POST /api/endpoints/{name}` forwards to — or,
   *  beginning with `/`, a route on the platform itself, posted to as written. Either names a door,
   *  not an outcome. */
  via: string;
  /** The archetype the minted Thing `is`, where a press mints one. Declared here rather than left to
   *  the endpoint so the seed rules that check every archetype a page names cover what it writes.
   *  A press that marks the row itself mints nothing and names none. */
  archetype?: string;
  /** The relationship from the minted Thing to the row it is about. */
  predicate?: string;
  /** Whether a row can be pressed again after an act was recorded against it. Absent, a row is
   *  decided once. An act that administers rather than decides — a grant, a password — is made as
   *  often as needed, and the answer to the last press is shown beside the row until the next. */
  repeatable?: boolean;
  choices: ActionChoice[];
}

/** A widget that records a decision rather than reporting one. Every other widget reads; this is
 *  the one that acts on what it lists. */
export interface ActionWidget {
  type: 'action';
  title?: string;
  hint?: string;
  /** The things a decision can be made about. */
  rows: Binding;
  /** What each row's name is read from, so a person sees what they are judging. */
  label?: string;
  /** What is drawn beside each name — a decision made by ringing somebody outside the system needs
   *  the row to say enough to act on, and a name alone is not enough. */
  shows?: string[];
  /** What a person supplies for a row before pressing. Read once for the widget, however many rows
   *  ask. */
  asks?: AskedValue[];
  writes: ActionRecords;
}

/** A widget that records something nothing on the page lists yet. Every other writing press is
 *  about a row; this one mints the row. Its fields are the values the endpoint needs, posted under
 *  the act's name. */
export interface FormWidget {
  type: 'form';
  title?: string;
  hint?: string;
  fields: AskedValue[];
  submit: string;
  /** A read of the same fields, offered beside the press that acts on them — what a choice covers
   *  before somebody commits to it. It posts what has been filled in so far under its own act and
   *  shows the endpoint's answer, so it is offered before every value the act needs has been given.
   *  Absent, nothing is offered and the form is the press alone. */
  preview?: { act: string; label: string };
  writes: {
    /** The endpoint that accepts the act — a door, not an outcome, exactly as on an action widget. */
    via: string;
    /** The name the endpoint accepts this act under, sent as `view`. */
    act: string;
    /** The archetype the minted Thing `is`. Absent where the door mints no Thing — a platform route
     *  that records an account rather than a Thing. */
    archetype?: string;
  };
}

/** The columns a grid section is laid out on. */
export const GRID_COLUMNS = 12;

/** Where a widget stands in a `grid` section: the column and row it starts at, counted from zero,
 *  and how many of each it spans. */
export interface Placement {
  column: number;
  row: number;
  width: number;
  height: number;
}

/** A widget in a `grid` section carries where it stands; in the three fixed layouts the section's
 *  rule places it and the placement is not read. */
export interface Placed {
  placement?: Placement;
}

export type Widget = Placed & (
  | KpiWidget
  | FunnelWidget
  | BulletWidget
  | TableWidget
  | GanttWidget
  | LeaderboardWidget
  | VerdictWidget
  | WorkingWidget
  | ExceptionWidget
  | RangeBarWidget
  | LineSeriesWidget
  | HeatmapWidget
  | StackedSharesWidget
  | DivergingBarWidget
  | SmallMultiplesWidget
  | ActionWidget
  | FormWidget
);

export interface DashboardSection {
  title?: string;
  hint?: string;
  /** 'kpi-strip' = equal columns; 'split' = weighted 2-col; 'single' = full width; 'grid' = each
   *  widget where its own placement says, on {@link GRID_COLUMNS} columns. */
  layout?: 'kpi-strip' | 'split' | 'single' | 'grid';
  /** Relative column widths for 'split'/'kpi-strip'. */
  widths?: number[];
  widgets: Widget[];
  /** The name of a {@link DeclaredTheme} the model holds. A page that draws themes draws this
   *  section as a tile faced in the theme's colour: its `kpi` widgets are the summary shown while the
   *  tile is hovered or focused, its other widgets the gallery a click opens, and a card in the
   *  gallery opens the widget full width. A section with no widgets is a muted tile reading "not
   *  assessed". A section naming no theme, and every section on a page that draws no themes, renders
   *  as a list exactly as before. */
  theme?: string;
  /** Marks the section whose `kpi` widgets are the facts about the land: the explore page draws them
   *  as cards beside the map — the figure, the title, and where the model says it came from in place
   *  of a tick — and not in the report beneath. The page draws the area, the coordinates and the
   *  reference from what it knows itself, so this section carries only what the page cannot know. */
  facts?: boolean;
  /** Names the section as a tab of the closing view over the land: the explore page draws every
   *  section carrying one as a tab in a sheet along the bottom of the map, in spec order, and not in
   *  the report beneath. The word is the tab's key; the section's title is what the tab reads. */
  tab?: string;
}

/** Declares the entity type compared in the scope switcher + leaderboard. */
export interface CompareConfiguration {
  label: string;
  archetype: string;
}

/**
 * One relation to surface on the detail card: which relationship to follow from the current Thing,
 * which related Thing to keep, and what of it to show. `relations` nest, so a card can walk
 * project → phase → task. Array order is the display order at every level.
 */
export interface RelationSpecification {
  predicate: string;
  /**
   * 'out': the current Thing is the subject, follow to the targets.
   * 'in': the current Thing is the target, follow to the subjects. Default 'out'.
   */
  direction?: 'out' | 'in';
  /**
   * Keep only related Things of this archetype (its `is`-target name). Omit to keep every
   * match — needed when one predicate (e.g. `has`) reaches several archetypes at once.
   */
  archetype?: string;
  /** Heading for the group. Defaults to the predicate name. */
  label?: string;
  /**
   * Which properties of the related Thing to show: a list of keys, or '*' for all.
   * Omit to show none (name and derived states only).
   */
  properties?: string[] | '*';
  /**
   * Fold this relation's matched Thing onto the parent row instead of rendering it as its own
   * nested card: its selected `properties` are hoisted onto the parent relationship. Use for a one-hop
   * lookup that belongs on the parent line — e.g. a task's resource code, which lives on the
   * referenced Resource. Ignored on a top-level relation (there is no parent row to fold onto).
   */
  inline?: boolean;
  /** Relations to follow from each matched Thing in turn. */
  relations?: RelationSpecification[];
}

/**
 * How to render a detail window for a single Thing when a row is clicked. Entirely model
 * vocabulary — Trellis reads the shape, the model supplies the property keys and predicate
 * names (like {@link ScopeRef.viaPredicate}). Absent → rows aren't clickable.
 */
export interface DetailSpecification {
  /** Property whose value titles the window. Falls back to the Thing's name. */
  titleProperty?: string;
  /** Property shown as a subtitle under the title. */
  subtitleProperty?: string;
  /** Curated property groups. Omit to show all own properties in one group. */
  propertyGroups?: { label: string; keys: string[] }[];
  /** Ordered relations to surface on the card, each optionally nesting further. */
  relations?: RelationSpecification[];
  /** Handling history — the root Thing's own derived-state changes over time. */
  history?: { enabled?: boolean };
}

/**
 * Per-locale translations of the spec's own display strings, keyed first by
 * locale code (`es`, `nl`, …) then by the BASE (as-authored) string. Trellis
 * renders a display string `s` as `translations[activeLocale]?.[s] ?? s`, so an
 * absent locale or an untranslated string falls back to the base text — never a
 * blank or a raw key. Only human-facing labels are looked up; model vocabulary
 * (state names, property keys, predicate names, archetypes) is never translated.
 * See `localizeSpec` in src/api/dashboardLocalization.ts and the authoring
 * contract in docs/FIELD_GUIDE.md, Part IX.
 */
export type SpecificationTranslations = Record<string, Record<string, string>>;

export interface DashboardSpecification {
  title: string;
  subtitle?: string;
  /** Name of the icon the navigation entry draws, from the set Trellis renders with — the same
   *  presentation vocabulary the spec already carries as colours and number formats. A spec that
   *  names none, or names one Trellis cannot draw, gets a generic icon rather than no entry. */
  icon?: string;
  compare?: CompareConfiguration;
  sections: DashboardSection[];
  /** Re-resolve every binding on this cadence, on top of the live-event refresh. Time-anchored
   *  widgets (a trailing-window trace) move even when nothing in the model changed. Omitted or 0
   *  leaves the page purely event-driven. */
  refreshSeconds?: number;
  /** Enables clickable rows that open a generic Thing detail window. */
  detail?: DetailSpecification;
  translations?: SpecificationTranslations;
  /** The choices a composed page was made from. Present only on a page the console kept, which is
   *  what lets it offer to rename or remove the page and leave a seeded one alone. */
  composed?: Composition;
  /** Marks a page the Design page kept. Like `composed`, it is what lets the console offer to
   *  rename or remove the page and leave a seeded one alone. */
  designed?: true;
}

export interface DashboardDescriptor {
  id: string;
  name: string;
  /** The URL segment this dashboard answers to, under `/operations`. Derived from the Thing's name
   *  so a link survives a reseeded model, and language-independent so an address does not change
   *  when the reader's language does. */
  routeKey: string;
  /** Null where the Thing carries a spec that could not be read. Such a dashboard is still listed
   *  and still addressable, so the author sees the fault rather than a page that is simply missing. */
  specification: DashboardSpecification | null;
}

export interface ScopeEntity {
  id: string;
  name: string;
}

/** A theme the model declares for a dashboard section to name, read by the mark its archetype carries
 *  and handed to a page as the model states it. `colour` is a CSS colour for the tile's face, `icon` a
 *  name from the icon set Trellis renders with, `order` the tile's place in the grid. Each is null
 *  where the model states none: a tile then takes a neutral face, no icon, and a place after every
 *  ordered tile. */
export interface DeclaredTheme {
  name: string;
  colour: string | null;
  icon: string | null;
  order: number | null;
}
