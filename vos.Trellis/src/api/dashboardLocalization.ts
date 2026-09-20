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
  Binding,
  BulletWidget,
  DashboardSection,
  DashboardSpec,
  DetailSpec,
  DivergingBarWidget,
  ExceptionWidget,
  FunnelWidget,
  GanttWidget,
  HeatmapWidget,
  KpiWidget,
  LeaderboardWidget,
  LineSeriesWidget,
  RangeBarWidget,
  RelationSpec,
  SmallMultiplesWidget,
  StackedSharesWidget,
  TableColumn,
  TableWidget,
  VerdictWidget,
  WorkingWidget,
  Widget,
  ActionWidget,
  AskedValue,
  FormWidget,
} from '../types/dashboard';
import { primarySubtag } from '../i18n/languages';

export type SpecTranslator = <T extends string | undefined>(text: T) => T;

/** The strings a locale reads: the primary-subtag block overlaid with the exact-tag
 *  one, so a model that authors `ar` once serves both `ar-SA` and `ar-AE`, and an
 *  `ar-AE` block need only carry the words that differ. */
function translationsFor(spec: DashboardSpec, locale: string): Record<string, string> | undefined {
  const regional = spec.translations?.[locale];
  const language = spec.translations?.[primarySubtag(locale)];
  if (!regional || !language || regional === language) return regional ?? language;
  return { ...language, ...regional };
}

export function makeSpecTranslator(spec: DashboardSpec, locale: string): SpecTranslator {
  const table = translationsFor(spec, locale);
  return (<T extends string | undefined>(text: T): T => {
    if (text === undefined || table === undefined) return text;
    return (table[text] ?? text) as T;
  }) as SpecTranslator;
}

function localizeColumns(columns: TableColumn[] | undefined, tr: SpecTranslator): TableColumn[] | undefined {
  return columns?.map((column) => ({ ...column, label: tr(column.label) }));
}

/** A verdict's and an origin's wording are the display strings the vocabulary keeps on a binding, so
 *  they are translated where every other binding value is not. What sits beside each stays as the
 *  model wrote it: a state name is resolved against the platform's derived states, and an origin is
 *  one of the four the model's declarations answer with. */
function localizeWording(binding: Binding | undefined, tr: SpecTranslator): Binding | undefined {
  if (binding?.kind === 'verdict') {
    return {
      ...binding,
      states: binding.states.map((c) => ({
        ...c,
        reads: tr(c.reads),
        ...(c.levers ? { levers: { raise: tr(c.levers.raise), lower: tr(c.levers.lower) } } : {}),
      })),
    };
  }
  if (binding?.kind === 'origin') {
    const reads = Object.fromEntries(
      Object.entries(binding.reads).map(([origin, wording]) => [origin, tr(wording)]),
    );
    return { ...binding, reads };
  }
  return binding;
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
        origin: localizeWording(widget.origin, tr),
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
    case 'working': {
      // The label and the unit are the spec's words. The formula and the input names are not — they are
      // the model's own property names, and a translation of one would name a property the model does
      // not have.
      const w: WorkingWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        rows: widget.rows.map((row) => ({ ...row, label: tr(row.label), unit: tr(row.unit) })),
      };
      return w;
    }
    case 'verdict': {
      const w: VerdictWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        rows: widget.rows.map((row) => ({
          ...row,
          label: tr(row.label),
          unit: tr(row.unit),
          verdicts: localizeWording(row.verdicts, tr) ?? row.verdicts,
        })),
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
    case 'rangeBar': {
      const w: RangeBarWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        unit: tr(widget.unit),
        bands: widget.bands?.map((band) => ({ ...band, label: tr(band.label) })),
      };
      return w;
    }
    case 'lineSeries': {
      const w: LineSeriesWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        unit: tr(widget.unit),
        series: widget.series.map((entry) => ({ ...entry, label: tr(entry.label) })),
      };
      return w;
    }
    case 'heatmap': {
      const w: HeatmapWidget = { ...widget, title: tr(widget.title), hint: tr(widget.hint), unit: tr(widget.unit) };
      return w;
    }
    case 'divergingBar': {
      const w: DivergingBarWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        unit: tr(widget.unit),
        up: { ...widget.up, label: tr(widget.up.label) },
        down: { ...widget.down, label: tr(widget.down.label) },
      };
      return w;
    }
    case 'smallMultiples': {
      const w: SmallMultiplesWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        bars: { ...widget.bars, label: tr(widget.bars.label), unit: tr(widget.bars.unit) },
        line: { ...widget.line, label: tr(widget.line.label), unit: tr(widget.line.unit) },
        band: widget.band ? { ...widget.band, label: tr(widget.band.label) } : undefined,
      };
      return w;
    }
    case 'stackedShares': {
      const w: StackedSharesWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        classes: widget.classes.map((entry) => ({ ...entry, label: tr(entry.label) })),
      };
      return w;
    }
    // A choice's label and a field's label are the spec's words; the act, the reason and the key
    // each is sent under are what the endpoint reads, and stay as written.
    case 'action': {
      const w: ActionWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        asks: localizeFields(widget.asks, tr),
        writes: { ...widget.writes, choices: widget.writes.choices.map((choice) => ({ ...choice, label: tr(choice.label) })) },
      };
      return w;
    }
    case 'form': {
      const w: FormWidget = {
        ...widget,
        title: tr(widget.title),
        hint: tr(widget.hint),
        fields: localizeFields(widget.fields, tr) ?? widget.fields,
        submit: tr(widget.submit),
        preview: widget.preview ? { ...widget.preview, label: tr(widget.preview.label) } : undefined,
      };
      return w;
    }
  }
}

function localizeFields(fields: AskedValue[] | undefined, tr: SpecTranslator): AskedValue[] | undefined {
  return fields?.map((field) => ({ ...field, label: tr(field.label) }));
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

function localizeWith(spec: DashboardSpec, tr: SpecTranslator): DashboardSpec {
  return {
    ...spec,
    title: tr(spec.title),
    subtitle: tr(spec.subtitle),
    compare: spec.compare ? { ...spec.compare, label: tr(spec.compare.label) } : undefined,
    sections: spec.sections.map((section) => localizeSection(section, tr)),
    detail: spec.detail ? localizeDetail(spec.detail, tr) : undefined,
  };
}

export function localizeSpec(spec: DashboardSpec, locale: string): DashboardSpec {
  if (!translationsFor(spec, locale)) return spec;
  return localizeWith(spec, makeSpecTranslator(spec, locale));
}

/** Every display string the localiser would translate, once each, in the order it walks them —
 *  the same walk, with a translator that records instead of translating, so a string the localiser
 *  reads is never one a translations panel forgot to offer. */
export function displayStringsOf(spec: DashboardSpec): string[] {
  const seen: string[] = [];
  const recording = (<T extends string | undefined>(text: T): T => {
    if (text && !seen.includes(text)) seen.push(text);
    return text;
  }) as SpecTranslator;
  localizeWith(spec, recording);
  return seen;
}
