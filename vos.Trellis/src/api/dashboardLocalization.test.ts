import { describe, it, expect } from 'vitest';
import { localizeSpec } from './dashboardLocalization';
import type {
  Binding,
  DashboardSpec,
  FunnelWidget,
  KpiWidget,
  LeaderboardWidget,
  TableWidget,
  VerdictWidget,
} from '../types/dashboard';

/** A spec exercising every widget type plus a detail card. The base strings are
 *  deliberately plain English; `translations.es` renders them in Spanish. One
 *  key (the exceptions section title) is left untranslated to prove key-level
 *  fallback, and the state name "Harvested" doubles as a KPI unit to prove that a
 *  binding value matching a translation entry is never rewritten. */
function fixture(): DashboardSpec {
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
    const spec = localizeSpec(fixture(), 'es');
    expect(spec.title).toBe('Operaciones del pueblo');
    expect(spec.subtitle).toBe('Vista en vivo');
    expect(spec.compare?.label).toBe('sitio');

    const [yieldSection, exceptions] = spec.sections;
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
    expect(spec.detail?.propertyGroups?.[0].label).toBe('Detalles');
    expect(spec.detail?.relations?.[0].label).toBe('Tareas');
    expect(spec.detail?.relations?.[0].relations?.[0].label).toBe('Recurso');
  });

  it('never translates binding values or model-vocabulary keys', () => {
    const spec = localizeSpec(fixture(), 'es');
    const kpi = spec.sections[0].widgets[0] as KpiWidget;
    // The state name stays "Harvested" even though the KPI unit "Harvested" was translatable.
    expect(kpi.value).toEqual({ kind: 'stateCount', state: 'Harvested' });
    // Row keys are identifiers, not labels — untouched.
    const table = spec.sections[0].widgets[2] as TableWidget;
    expect(table.columns.map((c) => c.key)).toEqual(['name', 'age']);
    expect(spec.detail?.relations?.[0].predicate).toBe('has');
  });

  it('falls back to the base text when the active locale is absent', () => {
    const spec = localizeSpec(fixture(), 'de');
    expect(spec.title).toBe('Village Operations');
    expect(spec.sections[0].title).toBe('Yield');
  });

  it('falls back to the base text for a key missing within a present locale', () => {
    const spec = localizeSpec(fixture(), 'es');
    // The "Exceptions" section title has no es entry.
    expect(spec.sections[1].title).toBe('Exceptions');
  });

  it('reads a language-level block for a regional locale', () => {
    const spec = localizeSpec(fixture(), 'es-MX');
    expect(spec.title).toBe('Operaciones del pueblo');
    expect(spec.sections[0].title).toBe('Rendimiento');
  });

  it('lets a regional block override single words without restating the language block', () => {
    const base = fixture();
    base.translations!['es-MX'] = { 'Village Operations': 'Operaciones del pueblito' };
    const spec = localizeSpec(base, 'es-MX');
    expect(spec.title).toBe('Operaciones del pueblito');
    expect(spec.sections[0].title).toBe('Rendimiento');
  });

  it('returns a spec with no translations block unchanged (no regression)', () => {
    const base = fixture();
    delete base.translations;
    const spec = localizeSpec(base, 'es');
    expect(spec).toBe(base);
  });
});

// The verdict wording is the only display text the spec vocabulary keeps on a binding, so the
// invariant that binding values pass through untouched has to bend for it — and only for it.
describe('localizeSpec over a verdict widget', () => {
  function verdictSpec(): DashboardSpec {
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
                      { state: 'EnergyShortOfTarget', reads: 'short of the {target} target' },
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
          EnergyShortOfTarget: 'NUNCA',
        },
      },
    };
  }

  const localized = localizeSpec(verdictSpec(), 'es').sections[0].widgets[0] as VerdictWidget;
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

  it('translates the widget and row labels around it', () => {
    expect(localized.title).toBe('Balances energéticos');
    expect(localized.hint).toBe('según se juzga');
    expect(localized.rows[0].label).toBe('Energía');
    expect(localized.rows[0].unit).toBe('días');
  });
});
