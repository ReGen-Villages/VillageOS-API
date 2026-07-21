/**
 * Render-time localization of a model-supplied {@link DashboardSpec}.
 *
 * A model author keeps writing the spec in one base language and adds a single
 * `translations` block: `{ [locale]: { [baseString]: translatedString } }`.
 * `localizeSpec` returns a copy of the spec with every human-facing label
 * replaced by its translation in the active locale, falling back to the base
 * text whenever the locale — or a given string within it — is absent.
 *
 * Only display strings are translated. Everything a binding resolves against —
 * state names, archetypes, property keys, predicate names, row keys, endpoints,
 * colours, formats — is model vocabulary and passes through untouched, so a
 * label that happens to match a binding value can never corrupt resolution.
 */
import type {
  BulletWidget,
  DashboardSection,
  DashboardSpec,
  DetailSpec,
  ExceptionWidget,
  FunnelWidget,
  GanttWidget,
  KpiWidget,
  LeaderboardWidget,
  RelationSpec,
  TableColumn,
  TableWidget,
  Widget,
} from '../types/dashboard';

/** Translates a single base string into the active locale, or returns it unchanged. */
export type SpecTranslator = <T extends string | undefined>(text: T) => T;

export function makeSpecTranslator(spec: DashboardSpec, locale: string): SpecTranslator {
  const table = spec.translations?.[locale];
  return (<T extends string | undefined>(text: T): T => {
    if (text === undefined || table === undefined) return text;
    return (table[text] ?? text) as T;
  }) as SpecTranslator;
}

function localizeColumns(columns: TableColumn[] | undefined, tr: SpecTranslator): TableColumn[] | undefined {
  return columns?.map((column) => ({ ...column, label: tr(column.label) }));
}

function localizeWidget(widget: Widget, tr: SpecTranslator): Widget {
  switch (widget.type) {
    case 'kpi': {
      const w: KpiWidget = {
        ...widget,
        title: tr(widget.title),
        unit: tr(widget.unit),
        targetLabel: tr(widget.targetLabel),
        sparkBaselineLabel: tr(widget.sparkBaselineLabel),
        footnote: tr(widget.footnote),
      };
      return w;
    }
    case 'funnel': {
      const w: FunnelWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        searchNoun: tr(widget.searchNoun),
        stages: widget.stages.map((stage) => ({
          ...stage,
          label: tr(stage.label),
          sublabel: tr(stage.sublabel),
        })),
        drillColumns: localizeColumns(widget.drillColumns, tr),
      };
      return w;
    }
    case 'bullet': {
      const w: BulletWidget = {
        ...widget,
        title: tr(widget.title),
        rows: widget.rows.map((row) => ({ ...row, label: tr(row.label) })),
      };
      return w;
    }
    case 'table': {
      const w: TableWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        columns: localizeColumns(widget.columns, tr) ?? widget.columns,
      };
      return w;
    }
    case 'gantt': {
      const w: GanttWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        ticks: widget.ticks?.map((tick) => tr(tick)),
      };
      return w;
    }
    case 'leaderboard': {
      const w: LeaderboardWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        metrics: widget.metrics.map((metric) => ({ ...metric, label: tr(metric.label) })),
      };
      return w;
    }
    case 'exceptionBar': {
      const w: ExceptionWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        note: tr(widget.note),
        buckets: widget.buckets.map((bucket) => ({ ...bucket, label: tr(bucket.label) })),
      };
      return w;
    }
  }
}

function localizeSection(section: DashboardSection, tr: SpecTranslator): DashboardSection {
  return {
    ...section,
    title: tr(section.title),
    hint: tr(section.hint),
    widgets: section.widgets.map((widget) => localizeWidget(widget, tr)),
  };
}

function localizeRelation(relation: RelationSpec, tr: SpecTranslator): RelationSpec {
  return {
    ...relation,
    label: tr(relation.label),
    relations: relation.relations?.map((child) => localizeRelation(child, tr)),
  };
}

function localizeDetail(detail: DetailSpec, tr: SpecTranslator): DetailSpec {
  return {
    ...detail,
    propertyGroups: detail.propertyGroups?.map((group) => ({ ...group, label: tr(group.label) })),
    relations: detail.relations?.map((relation) => localizeRelation(relation, tr)),
  };
}

/** Return a copy of the spec with every display string rendered in the active locale. */
export function localizeSpec(spec: DashboardSpec, locale: string): DashboardSpec {
  if (!spec.translations?.[locale]) return spec;
  const tr = makeSpecTranslator(spec, locale);
  return {
    ...spec,
    title: tr(spec.title),
    subtitle: tr(spec.subtitle),
    compare: spec.compare ? { ...spec.compare, label: tr(spec.compare.label) } : undefined,
    sections: spec.sections.map((section) => localizeSection(section, tr)),
    detail: spec.detail ? localizeDetail(spec.detail, tr) : undefined,
  };
}
