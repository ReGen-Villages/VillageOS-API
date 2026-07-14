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
  /** Rows of Things currently in a State, enriched with their properties for a table. */
  | { kind: 'stateList'; state: string; scope?: ScopeRef; limit?: number; archetype?: string }
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
  /** One row per compare-entity, carrying the listed numeric properties (leaderboard source). */
  | { kind: 'compareEntities'; properties: string[] }
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

export interface DashboardSpec {
  title: string;
  subtitle?: string;
  compare?: CompareConfig;
  sections: DashboardSection[];
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
