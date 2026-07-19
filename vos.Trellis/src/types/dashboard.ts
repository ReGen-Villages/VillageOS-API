/**
 * Generic, model-agnostic dashboard contract.
 *
 * Trellis ships the *widgets* and a *binding resolver*; the model supplies the
 * mapping. A model declares one or more Things of archetype `Dashboard`, each
 * carrying a `spec` property (JSON) that conforms to {@link DashboardSpec}.
 *
 * Nothing in this file names a domain (no "order", "warehouse", "picking", …).
 * Every domain word lives in the model's spec — see the discovery + resolver in
 * `src/api/dashboardApi.ts`.
 */

/** The archetype a model-resident dashboard config Thing must be `is`-linked to. */
export const DASHBOARD_ARCHETYPE = 'Dashboard';
/** The Thing property holding the JSON-encoded {@link DashboardSpec}. */
export const DASHBOARD_SPEC_PROPERTY = 'spec';

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
  | { kind: 'stateList'; state: string; excludeState?: string; scope?: ScopeRef; limit?: number; archetype?: string }
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
  /** One binding divided by another — a rate the aggregate ops cannot express, because a ratio of
   *  sums is not a sum of ratios. Resolves to null when the denominator is zero or non-numeric. */
  | { kind: 'ratio'; numerator: Binding; denominator: Binding }
  /** One row per compared Thing, carrying the listed numeric properties (leaderboard source).
   *  `computed` adds columns whose value is a Binding resolved once per Thing, with that Thing
   *  as the scope — so a column can hold a live aggregate over the Thing's members, not just a
   *  property stored on the Thing itself. */
  | { kind: 'compareEntities'; properties: string[]; computed?: ComputedColumn[] }
  /** A bucketed time series from the temporal API. Degrades to [] when history is absent. */
  | {
      kind: 'timeseries';
      archetype?: string;
      thing?: string;
      property?: string;
      op: 'count' | 'sum' | 'avg';
      bucket: 'hour' | 'day';
      buckets?: number;
    }
  /** Delegate to a model-side service via POST /api/endpoints/{subdomain}. The escape
   *  hatch for model-specific aggregation. `select` is a dot-path into the JSON reply. */
  | { kind: 'service'; endpoint: string; body?: unknown; select?: string };

/**
 * A `compareEntities` column derived per Thing rather than read from a stored property.
 * The binding resolves once per compared Thing, so prefer one that reads the model store
 * (`aggregate`, `property`, or a `ratio` over those). A binding that calls the broker —
 * `stateCount`, `stateList`, `timeseries`, `service` — costs one request per Thing on every
 * dashboard refresh.
 */
export interface ComputedColumn {
  /** Row key the column lands on — what a LeaderMetric/TableColumn references. */
  key: string;
  value: Binding;
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
  footnote?: string;
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
  /** 0..1 target marker. */
  target?: number;
  /** 0..1 healthy band [lo, hi]. */
  band?: [number, number];
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

export type Widget =
  | KpiWidget
  | FunnelWidget
  | BulletWidget
  | TableWidget
  | GanttWidget
  | LeaderboardWidget
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

export interface DashboardSpec {
  title: string;
  subtitle?: string;
  compare?: CompareConfig;
  sections: DashboardSection[];
  /** Enables clickable rows that open a generic Thing detail window. */
  detail?: DetailSpec;
}

/** A discovered, parsed dashboard: the source Thing + its validated spec. */
export interface DashboardDescriptor {
  id: string;
  name: string;
  spec: DashboardSpec;
}

/** A compare entity (e.g. a site or region) offered in the scope switcher. */
export interface ScopeEntity {
  id: string;
  name: string;
}
