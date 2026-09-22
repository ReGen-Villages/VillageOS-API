import { describe, it, expect } from 'vitest';
import { localizeSpecification, displayStringsOf } from './dashboardLocalization';
import type {
  Binding,
  DashboardSpecification,
  FunnelWidget,
  KpiWidget,
  LeaderboardWidget,
  TableWidget,
  VerdictWidget,
  RangeBarWidget,
  LineSeriesWidget,
  HeatmapWidget,
  StackedSharesWidget,
  DivergingBarWidget,
  SmallMultiplesWidget,
  ActionWidget,
  FormWidget,
} from '../types/dashboard';

/** A spec exercising every widget type plus a detail card. The base strings are
 *  deliberately plain English; `translations.es` renders them in Spanish. One
 *  key (the exceptions section title) is left untranslated to prove key-level
 *  fallback, and the state name "Harvested" doubles as a KPI unit to prove that a
 *  binding value matching a translation entry is never rewritten. */
function fixture(): DashboardSpecification {
  return {
    title: 'Village Operations',
    subtitle: 'Live view',
    compare: { label: 'site', archetype: 'Site' },
    sections: [
      {
        title: 'Yield',
        hint: 'last hour',
        widgets: [
          {
            type: 'kpi',
            title: 'Harvest logged',
            value: { kind: 'stateCount', state: 'Harvested' },
            unit: 'Harvested',
            targetLabel: 'goal',
            footnote: 'since midnight',
          } satisfies KpiWidget,
          {
            type: 'funnel',
            title: 'Growth',
            hint: 'by stage',
            searchNoun: 'harvests',
            stages: [{ label: 'Planted', sublabel: 'ready to grow', count: { kind: 'const', value: 1 } }],
            drillColumns: [{ key: 'name', label: 'Plot' }],
          } satisfies FunnelWidget,
          {
            type: 'table',
            title: 'Backlog',
            hint: 'oldest first',
            columns: [{ key: 'name', label: 'Plot' }, { key: 'age', label: 'Age' }],
            rows: { kind: 'stateList', state: 'Open' },
          } satisfies TableWidget,
          {
            type: 'bullet',
            title: 'Targets',
            rows: [{ label: 'Fill rate', value: { kind: 'const', value: 0.9 } }],
          },
          {
            type: 'leaderboard',
            title: 'Sites',
            entities: { kind: 'compareEntities', properties: ['yield'] },
            metrics: [{ key: 'yield', label: 'Yield' }],
          } satisfies LeaderboardWidget,
          {
            type: 'gantt',
            title: 'Schedule',
            rows: { kind: 'const', value: 0 },
            ticks: ['Morning', 'Afternoon'],
          },
        ],
      },
      {
        title: 'Exceptions',
        widgets: [
          {
            type: 'exceptionBar',
            title: 'Problems',
            note: 'needs attention',
            buckets: [{ label: 'Late', value: { kind: 'const', value: 2 }, severity: 'warn' }],
          },
        ],
      },
    ],
    detail: {
      propertyGroups: [{ label: 'Details', keys: ['status'] }],
      relations: [{ predicate: 'has', label: 'Tasks', relations: [{ predicate: 'for', label: 'Resource' }] }],
    },
    translations: {
      es: {
        'Village Operations': 'Operaciones del pueblo',
        'Live view': 'Vista en vivo',
        site: 'sitio',
        Yield: 'Rendimiento',
        'last hour': 'última hora',
        'Harvest logged': 'Cosecha registrada',
        goal: 'objetivo',
        'since midnight': 'desde medianoche',
        Growth: 'Crecimiento',
        'by stage': 'por etapa',
        harvests: 'cosechas',
        Planted: 'Plantado',
        'ready to grow': 'listo para crecer',
        Plot: 'Parcela',
        Backlog: 'Pendientes',
        'oldest first': 'más antiguos primero',
        Age: 'Antigüedad',
        Targets: 'Objetivos',
        'Fill rate': 'Tasa de ocupación',
        Sites: 'Sitios',
        Schedule: 'Horario',
        Morning: 'Mañana',
        Afternoon: 'Tarde',
        Problems: 'Problemas',
        'needs attention': 'requiere atención',
        Late: 'Retrasado',
        Details: 'Detalles',
        Tasks: 'Tareas',
        Resource: 'Recurso',
        // "Yield" (metric label) shares the section-title translation above.
        // "Harvested" is intentionally absent: it is a state name / binding value,
        // never a translatable label.
      },
    },
  };
}

describe('localizeSpec', () => {
  it('renders display strings in the active locale', () => {
    const specification = localizeSpecification(fixture(), 'es');
    expect(specification.title).toBe('Operaciones del pueblo');
    expect(specification.subtitle).toBe('Vista en vivo');
    expect(specification.compare?.label).toBe('sitio');

    const [yieldSection, exceptions] = specification.sections;
    expect(yieldSection.title).toBe('Rendimiento');
    expect(yieldSection.hint).toBe('última hora');

    const kpi = yieldSection.widgets[0] as KpiWidget;
    expect(kpi.title).toBe('Cosecha registrada');
    expect(kpi.targetLabel).toBe('objetivo');
    expect(kpi.footnote).toBe('desde medianoche');

    const funnel = yieldSection.widgets[1] as FunnelWidget;
    expect(funnel.title).toBe('Crecimiento');
    expect(funnel.searchNoun).toBe('cosechas');
    expect(funnel.stages[0].label).toBe('Plantado');
    expect(funnel.stages[0].sublabel).toBe('listo para crecer');
    expect(funnel.drillColumns?.[0].label).toBe('Parcela');

    const table = yieldSection.widgets[2] as TableWidget;
    expect(table.columns.map((c) => c.label)).toEqual(['Parcela', 'Antigüedad']);

    const leaderboard = yieldSection.widgets[4] as LeaderboardWidget;
    expect(leaderboard.metrics[0].label).toBe('Rendimiento');

    expect(exceptions.widgets[0].title).toBe('Problemas');
    expect(specification.detail?.propertyGroups?.[0].label).toBe('Detalles');
    expect(specification.detail?.relations?.[0].label).toBe('Tareas');
    expect(specification.detail?.relations?.[0].relations?.[0].label).toBe('Recurso');
  });

  it('never translates binding values or model-vocabulary keys', () => {
    const specification = localizeSpecification(fixture(), 'es');
    const kpi = specification.sections[0].widgets[0] as KpiWidget;
    // The state name stays "Harvested" even though the KPI unit "Harvested" was translatable.
    expect(kpi.value).toEqual({ kind: 'stateCount', state: 'Harvested' });
    // Row keys are identifiers, not labels — untouched.
    const table = specification.sections[0].widgets[2] as TableWidget;
    expect(table.columns.map((c) => c.key)).toEqual(['name', 'age']);
    expect(specification.detail?.relations?.[0].predicate).toBe('has');
  });

  it('falls back to the base text when the active locale is absent', () => {
    const specification = localizeSpecification(fixture(), 'de');
    expect(specification.title).toBe('Village Operations');
    expect(specification.sections[0].title).toBe('Yield');
  });

  it('falls back to the base text for a key missing within a present locale', () => {
    const specification = localizeSpecification(fixture(), 'es');
    // The "Exceptions" section title has no es entry.
    expect(specification.sections[1].title).toBe('Exceptions');
  });

  it('reads a language-level block for a regional locale', () => {
    const specification = localizeSpecification(fixture(), 'es-MX');
    expect(specification.title).toBe('Operaciones del pueblo');
    expect(specification.sections[0].title).toBe('Rendimiento');
  });

  it('lets a regional block override single words without restating the language block', () => {
    const base = fixture();
    base.translations!['es-MX'] = { 'Village Operations': 'Operaciones del pueblito' };
    const specification = localizeSpecification(base, 'es-MX');
    expect(specification.title).toBe('Operaciones del pueblito');
    expect(specification.sections[0].title).toBe('Rendimiento');
  });

  it('returns a spec with no translations block unchanged (no regression)', () => {
    const base = fixture();
    delete base.translations;
    const specification = localizeSpecification(base, 'es');
    expect(specification).toBe(base);
  });
});

// The verdict wording is the only display text the spec vocabulary keeps on a binding, so the
// invariant that binding values pass through untouched has to bend for it — and only for it.
describe('localizeSpec over a verdict widget', () => {
  function verdictSpecification(): DashboardSpecification {
    return {
      title: 'Analysis',
      sections: [
        {
          widgets: [
            {
              type: 'verdict',
              title: 'Balances',
              hint: 'as judged',
              rows: [
                {
                  label: 'Energy',
                  unit: 'days',
                  verdicts: {
                    kind: 'verdict',
                    states: [
                      {
                        state: 'EnergyShortOfTarget',
                        reads: 'short of the {target} target',
                        levers: { raise: 'more {term}', lower: 'less {term}' },
                      },
                      { state: 'EnergyNotAssessed', reads: 'not assessed' },
                    ],
                  },
                },
              ],
            } satisfies VerdictWidget,
          ],
        },
      ],
      translations: {
        es: {
          Balances: 'Balances energéticos',
          'as judged': 'según se juzga',
          Energy: 'Energía',
          days: 'días',
          'short of the {target} target': 'por debajo del objetivo de {target}',
          'more {term}': 'más {term}',
          'less {term}': 'menos {term}',
          EnergyShortOfTarget: 'NUNCA',
        },
      },
    };
  }

  const localized = localizeSpecification(verdictSpecification(), 'es').sections[0].widgets[0] as VerdictWidget;
  const binding = localized.rows[0].verdicts as Extract<Binding, { kind: 'verdict' }>;

  it('translates the wording each verdict reads as', () => {
    expect(binding.states[0].reads).toBe('por debajo del objetivo de {target}');
  });

  it('leaves the placeholders in place for the figures to fill', () => {
    expect(binding.states[0].reads).toContain('{target}');
  });

  it('falls back to the base wording a locale does not carry', () => {
    expect(binding.states[1].reads).toBe('not assessed');
  });

  // A state name is model vocabulary the platform resolves against, never display text.
  it('never rewrites the state name, even when a translation entry matches it', () => {
    expect(binding.states[0].state).toBe('EnergyShortOfTarget');
  });

  // The lever wording is display text the way `reads` is; the `{term}` placeholder stays for the
  // model's own property name, which is never translated.
  it('translates the lever wording a state declares, keeping its placeholder', () => {
    expect(binding.states[0].levers).toEqual({ raise: 'más {term}', lower: 'menos {term}' });
  });

  it('translates the widget and row labels around it', () => {
    expect(localized.title).toBe('Balances energéticos');
    expect(localized.hint).toBe('según se juzga');
    expect(localized.rows[0].label).toBe('Energía');
    expect(localized.rows[0].unit).toBe('días');
  });
});

describe('localizeSpec over an origin binding', () => {
  const localized = localizeSpecification({
    title: 'Analysis',
    sections: [
      {
        widgets: [
          {
            type: 'kpi',
            title: 'Rainfall',
            value: { kind: 'property', thing: '$scope', property: 'rainfallMillimetresPerYear' },
            origin: {
              kind: 'origin',
              property: 'rainfallMillimetresPerYear',
              reads: {
                measured: 'resolved {resolvedAt} from {source}',
                unknown: 'origin not recorded',
              },
            },
          } satisfies KpiWidget,
        ],
      },
    ],
    translations: {
      es: {
        Rainfall: 'Lluvia',
        'resolved {resolvedAt} from {source}': 'obtenido el {resolvedAt} de {source}',
        rainfallMillimetresPerYear: 'NUNCA',
        measured: 'NUNCA',
      },
    },
  }, 'es').sections[0].widgets[0] as KpiWidget;
  const binding = localized.origin as Extract<Binding, { kind: 'origin' }>;

  it('translates the wording each origin reads as, placeholders intact', () => {
    expect(binding.reads.measured).toBe('obtenido el {resolvedAt} de {source}');
  });

  it('falls back to the base wording a locale does not carry', () => {
    expect(binding.reads.unknown).toBe('origin not recorded');
  });

  // The origin names and the property are what the model is read by, never display text.
  it('never rewrites the origin the wording is keyed by, nor the property it reads', () => {
    expect(Object.keys(binding.reads)).toEqual(['measured', 'unknown']);
    expect(binding.property).toBe('rainfallMillimetresPerYear');
  });
});

describe('localizeSpec over a rangeBar widget', () => {
  const specification: DashboardSpecification = {
    title: 'Analysis',
    sections: [{ widgets: [{
              type: 'rangeBar',
              title: 'Temperature',
              hint: 'by month',
              unit: 'degrees',
              months: {
                recordedHigh: { kind: 'const', value: 1 }, designHigh: { kind: 'const', value: 1 },
                averageHigh: { kind: 'const', value: 1 }, mean: { kind: 'const', value: 1 },
                averageLow: { kind: 'const', value: 1 }, designLow: { kind: 'const', value: 1 },
                recordedLow: { kind: 'const', value: 1 },
              },
              bands: [{ label: 'Comfort', colour: 'Comfort' }],
            } satisfies RangeBarWidget] }],
    translations: {
      es: {
          'Temperature': 'Temperatura',
          'by month': 'por mes',
          'degrees': 'grados',
          'Comfort': 'Confort',
      },
    },
  };
  const localized = localizeSpecification(specification, 'es').sections[0].widgets[0] as RangeBarWidget;

  it('translates the wording the widget shows', () => {
    expect(localized.title).toBe('Temperatura');
    expect(localized.hint).toBe('por mes');
    expect(localized.unit).toBe('grados');
    expect(localized.bands![0].label).toBe('Confort');
  });

  it('never rewrites a value that is not wording, even when a translation entry matches it', () => {
    expect(localized.bands![0].colour).toBe('Comfort');
  });
});

describe('localizeSpec over a lineSeries widget', () => {
  const specification: DashboardSpecification = {
    title: 'Analysis',
    sections: [{ widgets: [{
              type: 'lineSeries',
              title: 'Rain',
              hint: 'monthly',
              unit: 'millimetres',
              series: [{ label: 'This year', value: { kind: 'const', value: 1 } }],
            } satisfies LineSeriesWidget] }],
    translations: {
      es: {
          'Rain': 'Lluvia',
          'monthly': 'mensual',
          'millimetres': 'milímetros',
          'This year': 'Este año',
      },
    },
  };
  const localized = localizeSpecification(specification, 'es').sections[0].widgets[0] as LineSeriesWidget;

  it('translates the wording the widget shows', () => {
    expect(localized.title).toBe('Lluvia');
    expect(localized.hint).toBe('mensual');
    expect(localized.unit).toBe('milímetros');
    expect(localized.series[0].label).toBe('Este año');
  });
});

describe('localizeSpec over a heatmap widget', () => {
  const specification: DashboardSpecification = {
    title: 'Analysis',
    sections: [{ widgets: [{
              type: 'heatmap',
              title: 'Sun',
              hint: 'hour by day',
              unit: 'watts',
              value: { kind: 'const', value: 1 },
            } satisfies HeatmapWidget] }],
    translations: {
      es: {
          'Sun': 'Sol',
          'hour by day': 'hora por día',
          'watts': 'vatios',
      },
    },
  };
  const localized = localizeSpecification(specification, 'es').sections[0].widgets[0] as HeatmapWidget;

  it('translates the wording the widget shows', () => {
    expect(localized.title).toBe('Sol');
    expect(localized.hint).toBe('hora por día');
    expect(localized.unit).toBe('vatios');
  });
});

describe('localizeSpec over a stackedShares widget', () => {
  const specification: DashboardSpecification = {
    title: 'Analysis',
    sections: [{ widgets: [{
              type: 'stackedShares',
              title: 'Cover',
              hint: 'by month',
              classes: [{ label: 'Trees', colour: 'Trees', share: { kind: 'const', value: 1 } }],
            } satisfies StackedSharesWidget] }],
    translations: {
      es: {
          'Cover': 'Cubierta',
          'by month': 'por mes',
          'Trees': 'Árboles',
      },
    },
  };
  const localized = localizeSpecification(specification, 'es').sections[0].widgets[0] as StackedSharesWidget;

  it('translates the wording the widget shows', () => {
    expect(localized.title).toBe('Cubierta');
    expect(localized.hint).toBe('por mes');
    expect(localized.classes[0].label).toBe('Árboles');
  });

  it('never rewrites a value that is not wording, even when a translation entry matches it', () => {
    expect(localized.classes[0].colour).toBe('Trees');
  });
});

describe('localizeSpec over a divergingBar widget', () => {
  const specification: DashboardSpecification = {
    title: 'Analysis',
    sections: [{ widgets: [{
              type: 'divergingBar',
              title: 'Balance',
              hint: 'by month',
              unit: 'litres',
              up: { label: 'Gain', value: { kind: 'const', value: 1 } },
              down: { label: 'Loss', value: { kind: 'const', value: 1 } },
            } satisfies DivergingBarWidget] }],
    translations: {
      es: {
          'Balance': 'Balance hídrico',
          'by month': 'por mes',
          'litres': 'litros',
          'Gain': 'Ganancia',
          'Loss': 'Pérdida',
      },
    },
  };
  const localized = localizeSpecification(specification, 'es').sections[0].widgets[0] as DivergingBarWidget;

  it('translates the wording the widget shows', () => {
    expect(localized.title).toBe('Balance hídrico');
    expect(localized.hint).toBe('por mes');
    expect(localized.unit).toBe('litros');
    expect(localized.up.label).toBe('Ganancia');
    expect(localized.down.label).toBe('Pérdida');
  });
});

describe('localizeSpec over a smallMultiples widget', () => {
  const specification: DashboardSpecification = {
    title: 'Analysis',
    sections: [{ widgets: [{
              type: 'smallMultiples',
              title: 'Months',
              hint: 'twelve panels',
              bars: { label: 'Rain', unit: 'millimetres', value: { kind: 'const', value: 1 } },
              line: { label: 'Heat', unit: 'degrees', value: { kind: 'const', value: 1 } },
              band: { label: 'Comfort', colour: 'Comfort' },
            } satisfies SmallMultiplesWidget] }],
    translations: {
      es: {
          'Months': 'Meses',
          'twelve panels': 'doce paneles',
          'Rain': 'Lluvia',
          'millimetres': 'milímetros',
          'Heat': 'Calor',
          'degrees': 'grados',
          'Comfort': 'Confort',
      },
    },
  };
  const localized = localizeSpecification(specification, 'es').sections[0].widgets[0] as SmallMultiplesWidget;

  it('translates the wording the widget shows', () => {
    expect(localized.title).toBe('Meses');
    expect(localized.hint).toBe('doce paneles');
    expect(localized.bars.label).toBe('Lluvia');
    expect(localized.bars.unit).toBe('milímetros');
    expect(localized.line.label).toBe('Calor');
    expect(localized.line.unit).toBe('grados');
    expect(localized.band!.label).toBe('Confort');
  });

  it('never rewrites a value that is not wording, even when a translation entry matches it', () => {
    expect(localized.band!.colour).toBe('Comfort');
  });
});

describe('localizeSpec over the writing widgets', () => {
  const specification: DashboardSpecification = {
    title: 'Springs',
    sections: [
      {
        widgets: [
          {
            type: 'action',
            title: 'Awaiting a verdict',
            rows: { kind: 'stateList', state: 'sampled' },
            asks: [{ key: 'reader', label: 'Reader' }],
            writes: { via: 'verdicts', choices: [{ label: 'Potable', target: 'Potable' }, { label: 'Close it', act: 'close' }] },
          },
          {
            type: 'form',
            title: 'Book a reading',
            fields: [{ key: 'litres', label: 'Litres', kind: 'number' }],
            submit: 'Book',
            preview: { act: 'cover', label: 'What this covers' },
            writes: { via: 'readings', act: 'book', archetype: 'Reading' },
          },
        ],
      },
    ],
    translations: {
      nl: { 'Awaiting a verdict': 'Wacht op oordeel', Reader: 'Lezer', Potable: 'Drinkbaar', 'Close it': 'Sluiten', 'Book a reading': 'Meting boeken', Litres: 'Liter', Book: 'Boek', 'What this covers': 'Wat dit dekt', close: 'NOOIT', book: 'NOOIT' },
    },
  };
  const [action, form] = localizeSpecification(specification, 'nl').sections[0].widgets as [ActionWidget, FormWidget];

  it('translates the labels a person reads and leaves the act and the reason as the endpoint reads them', () => {
    expect(action.title).toBe('Wacht op oordeel');
    expect(action.asks?.[0].label).toBe('Lezer');
    expect(action.writes.choices.map((choice) => choice.label)).toEqual(['Drinkbaar', 'Sluiten']);
    expect(action.writes.choices[1].act).toBe('close');
    expect(action.writes.choices[0].target).toBe('Potable');
  });

  it('translates a form’s field labels, button and preview label, and not its act', () => {
    expect(form.fields[0].label).toBe('Liter');
    expect(form.submit).toBe('Boek');
    expect(form.preview?.label).toBe('Wat dit dekt');
    expect(form.writes.act).toBe('book');
  });
});

describe('displayStringsOf', () => {
  it('lists every display string the localiser translates, once each, in the order it walks them', () => {
    const specification: DashboardSpecification = {
      title: 'Springs',
      subtitle: 'by catchment',
      compare: { label: 'Catchment', archetype: 'Catchment' },
      sections: [
        {
          title: 'Flow',
          hint: 'litres a second',
          widgets: [
            { type: 'kpi', title: 'Flowing', unit: 'l/s', value: { kind: 'const', value: 1 } },
            { type: 'table', title: 'Springs', columns: [{ key: 'flow', label: 'Flow' }], rows: { kind: 'const', value: 0 } as never },
          ],
        },
      ],
      detail: { propertyGroups: [{ label: 'Readings', keys: ['flow'] }], relations: [{ predicate: 'feeds', label: 'Feeds' }] },
    };
    expect(displayStringsOf(specification)).toEqual(['Springs', 'by catchment', 'Catchment', 'Flow', 'litres a second', 'Flowing', 'l/s', 'Springs', 'Flow', 'Readings', 'Feeds'].filter((word, at, all) => all.indexOf(word) === at));
  });

  it('agrees with what a translation reaches: every string it lists is one a translation of that string changes', () => {
    const specification: DashboardSpecification = {
      title: 'Springs',
      sections: [{ title: 'Flow', widgets: [{ type: 'kpi', title: 'Flowing', footnote: 'sampled hourly', value: { kind: 'const', value: 1 } }] }],
    };
    const translations = { nl: Object.fromEntries(displayStringsOf(specification).map((base) => [base, `${base} (nl)`])) };
    const localized = localizeSpecification({ ...specification, translations }, 'nl');
    expect(localized.title).toBe('Springs (nl)');
    expect(localized.sections[0].title).toBe('Flow (nl)');
    expect((localized.sections[0].widgets[0] as { footnote?: string }).footnote).toBe('sampled hourly (nl)');
  });
});
