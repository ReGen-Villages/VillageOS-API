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
import type { OriginKind } from './vos';

/** The archetype a model-resident dashboard config Thing must be `is`-linked to. */
export const DASHBOARD_ARCHETYPE = 'Dashboard';
/** The Thing property holding the JSON-encoded {@link DashboardSpec}. */
export const DASHBOARD_SPEC_PROPERTY = 'spec';
/** The spec's reference to "the compare entity currently selected in the scope switcher", which
 *  inside a computed column is the row's own Thing. */
export const SCOPE_REF = '$scope';
/** The edge saying what a Thing is. Every reader here follows it to resolve a type's members and
 *  the values they inherit, so both the walk and the subscription that has to carry it name the
 *  same predicate. */
export const IS_PREDICATE = 'is';

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

/**
 * A Binding is *how a widget slot gets its number/rows*. The `kind` set is
 * generic; the values (`state: 'active'`, `archetype: 'Device'`, property names)
 * are model-specific and come from the spec.
 *
 * The special thing reference `$scope` resolves to the compare-entity currently
 * selected in the page's scope switcher (e.g. the selected site), or is
 * averaged across all compare entities when "All" is selected.
 */
export type Binding =
  | { kind: 'const'; value: number }
  /** Count of Things currently in a derived State (via GET /api/states/{state}/things).
   *  `archetype` narrows the count to Things of that archetype (e.g. only Orders, not their lines). */
  | { kind: 'stateCount'; state: string; scope?: ScopeRef; archetype?: string }
  /** Rows of Things currently in a State, enriched with their properties for a table.
   *  `excludeState` drops Things also in that state — for a funnel stage, set it to the next
   *  stage's state so the list shows only Things that reached this stage and no further. */
  | {
      kind: 'stateList';
      state: string;
      excludeState?: string;
      scope?: ScopeRef;
      limit?: number;
      archetype?: string;
      computed?: ComputedColumn[];
    }
  /** Rows of every Thing of an archetype, whatever state each is in — the roster a `stateList`
   *  cannot express, since a Thing in no derived state appears in no state's list. Read from the
   *  client-side model index, so instances only: a sub-archetype is descended into, never listed.
   *  Rows are ordered by name, which is what makes a `limit`ed list the same list every time. */
  | { kind: 'thingList'; archetype: string; scope?: ScopeRef; limit?: number; computed?: ComputedColumn[] }
  /** Aggregate over Things of an archetype held in the model store (client-side). */
  | {
      kind: 'aggregate';
      archetype: string;
      op: 'count' | 'sum' | 'avg' | 'min' | 'max';
      property?: string;
      where?: PropertyFilter[];
      scope?: ScopeRef;
    }
  /** A single property of a named/id'd Thing, or of the selected scope entity (`$scope`). */
  | { kind: 'property'; thing: string; property: string }
  /** What a Thing's edges say rather than what its own properties store: follow `via` from the
   *  starting Thing and read the name of what that reaches, or the named `property` of it. The
   *  archetype a Thing `is`, the location it is `at`, the zone it `operates_in` — each is one
   *  step; a value two edges away is two. `thing` names the starting Thing and defaults to the
   *  selected scope entity (`$scope`), which inside a computed column is the row's own Thing.
   *  A lone numeric value resolves as a number so it can feed a numeric column or a KPI;
   *  several matches resolve as their names in alphabetical order, joined with ", "; none
   *  resolves to null. */
  | { kind: 'related'; via: RelationStep[]; thing?: string; property?: string }
  /** The derived state a Thing currently holds, as a word for a status cell. `states` lists the
   *  candidates in priority order and the first one the Thing holds wins — derived states nest
   *  (a harvested plot is also planted), so a single-valued cell needs the model to say which
   *  state answers the question. Holding none of them resolves to null.
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
   *  hang off — a page about a site reads verdicts off the study that studies it. A walk reaching
   *  several judged Things reports every one, in name order.
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
      scope?: ScopeRef;
    }
  /** Delegate to a model-side service via POST /api/endpoints/{subdomain}. The escape
   *  hatch for model-specific aggregation. `select` is a dot-path into the JSON reply, and a
   *  `$scope` anywhere in `body` is replaced with the selected compare-entity id (null for "All"),
   *  so the service can answer for the same entity the rest of the page is showing. */
  | { kind: 'service'; endpoint: string; body?: unknown; select?: string };

/**
 * A column of a row-producing binding (`thingList`, `stateList`, `compareEntities`) derived per
 * row rather than read from a stored property. The binding resolves once per row with that row's
 * Thing as the scope, so `$scope` in it means "this row's Thing" — which is how a column holds
 * a live aggregate over the Thing's members (`aggregate`, `ratio`), what its edges reach
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
 * which edge to follow from the Things reached so far, and which of the Things it reaches to keep.
 * Unlike {@link RelationSpec}, which describes how to render a related Thing on a detail card, a
 * step only narrows a walk down to the neighbour the binding means.
 */
export interface RelationStep {
  /** Predicate name to follow. */
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
export interface ScopeRef {
  /** Predicate name linking the scope entity to the counted Things. */
  viaPredicate: string;
  /** 'out' = scope is the Subject, 'in' = scope is the Target. Default 'out'. */
  direction?: 'out' | 'in';
}

// ---- Widgets -------------------------------------------------------------

export interface KpiWidget {
  type: 'kpi';
  title: string;
  value: Binding;
  format?: NumberFormat;
  unit?: string;
  target?: number;
  targetLabel?: string;
  /** Which direction is "good" for the delta + status colouring. Default 'up-good'. */
  direction?: 'up-good' | 'down-good';
  /** Resolves to a signed change vs. the prior period. */
  delta?: Binding;
  /** Resolves to number[] for the trend sparkline. */
  spark?: Binding;
  /** Resolves to the number the sparkline is read against — drawn as a dashed reference line. */
  sparkBaseline?: Binding;
  /** Label for that line, shown under the sparkline. */
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
  /** Value at/above which the metric is "good" (for a 0..1 normalisation). */
  best?: number;
  /** Value at/below which the metric is "worst". */
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

export type Widget =
  | KpiWidget
  | FunnelWidget
  | BulletWidget
  | TableWidget
  | GanttWidget
  | LeaderboardWidget
  | VerdictWidget
  | ExceptionWidget;

export interface DashboardSection {
  title?: string;
  hint?: string;
  /** 'kpi-strip' = equal columns; 'split' = weighted 2-col; 'single' = full width. */
  layout?: 'kpi-strip' | 'split' | 'single';
  /** Relative column widths for 'split'/'kpi-strip'. */
  widths?: number[];
  widgets: Widget[];
}

/** Declares the entity type compared in the scope switcher + leaderboard. */
export interface CompareConfig {
  /** Human label for one entity, e.g. "site". */
  label: string;
  /** Archetype of the entities being compared. */
  archetype: string;
}

/**
 * One relation to surface on the detail card: which edge to follow from the current Thing,
 * which related Thing to keep, and what of it to show. `relations` nest, so a card can walk
 * Order → line → allocation. Array order is the display order at every level.
 */
export interface RelationSpec {
  /** Predicate name to follow from the current Thing. */
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
   * nested card: its selected `properties` are hoisted onto the parent edge. Use for a one-hop
   * lookup that belongs on the parent line — e.g. an order line's item number, which lives on the
   * referenced Item. Ignored on a top-level relation (there is no parent row to fold onto).
   */
  inline?: boolean;
  /** Relations to follow from each matched Thing in turn. */
  relations?: RelationSpec[];
}

/**
 * How to render a detail window for a single Thing when a row is clicked. Entirely model
 * vocabulary — Trellis reads the shape, the model supplies the property keys and predicate
 * names (like {@link ScopeRef.viaPredicate}). Absent → rows aren't clickable.
 */
export interface DetailSpec {
  /** Property whose value titles the window. Falls back to the Thing's name. */
  titleProperty?: string;
  /** Property shown as a subtitle under the title. */
  subtitleProperty?: string;
  /** Curated property groups. Omit to show all own properties in one group. */
  propertyGroups?: { label: string; keys: string[] }[];
  /** Ordered relations to surface on the card, each optionally nesting further. */
  relations?: RelationSpec[];
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
 * contract in docs/TRELLIS.md.
 */
export type SpecTranslations = Record<string, Record<string, string>>;

export interface DashboardSpec {
  title: string;
  subtitle?: string;
  /** Name of the icon the navigation entry draws, from the set Trellis renders with — the same
   *  presentation vocabulary the spec already carries as colours and number formats. A spec that
   *  names none, or names one Trellis cannot draw, gets a generic icon rather than no entry. */
  icon?: string;
  compare?: CompareConfig;
  sections: DashboardSection[];
  /** Re-resolve every binding on this cadence, on top of the live-event refresh. Time-anchored
   *  widgets (a trailing-window trace) move even when nothing in the model changed. Omitted or 0
   *  leaves the page purely event-driven. */
  refreshSeconds?: number;
  /** Enables clickable rows that open a generic Thing detail window. */
  detail?: DetailSpec;
  /** Optional per-locale translations of this spec's display strings. */
  translations?: SpecTranslations;
}

/** A discovered, parsed dashboard: the source Thing + its validated spec. */
export interface DashboardDescriptor {
  id: string;
  name: string;
  /** The URL segment this dashboard answers to, under `/operations`. Derived from the Thing's name
   *  so a link survives a reseeded model, and language-independent so an address does not change
   *  when the reader's language does. */
  routeKey: string;
  spec: DashboardSpec;
}

/** A compare entity (e.g. a site or region) offered in the scope switcher. */
export interface ScopeEntity {
  id: string;
  name: string;
}
