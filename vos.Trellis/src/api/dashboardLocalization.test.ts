import { describe, it, expect } from 'vitest';
import { localizeSpec } from './dashboardLocalization';
import type { DashboardSpec, FunnelWidget, KpiWidget, LeaderboardWidget, TableWidget } from '../types/dashboard';

/** A spec exercising every widget type plus a detail card. The base strings are
 *  deliberately plain English; `translations.es` renders them in Spanish. One
 *  key (the exceptions section title) is left untranslated to prove key-level
 *  fallback, and the state name "Shipped" doubles as a KPI unit to prove that a
 *  binding value matching a translation entry is never rewritten. */
function fixture(): DashboardSpec {
  return {
    title: 'Order Operations',
    subtitle: 'Live view',
    compare: { label: 'site', archetype: 'Site' },
    sections: [
      {
        title: 'Throughput',
        hint: 'last hour',
        widgets: [
          {
            type: 'kpi',
            title: 'Orders shipped',
            value: { kind: 'stateCount', state: 'Shipped' },
            unit: 'Shipped',
            targetLabel: 'goal',
            footnote: 'since midnight',
          } satisfies KpiWidget,
          {
            type: 'funnel',
            title: 'Fulfilment',
            hint: 'by stage',
            searchNoun: 'orders',
            stages: [{ label: 'Picked', sublabel: 'ready to pack', count: { kind: 'const', value: 1 } }],
            drillColumns: [{ key: 'name', label: 'Order' }],
          } satisfies FunnelWidget,
          {
            type: 'table',
            title: 'Backlog',
            hint: 'oldest first',
            columns: [{ key: 'name', label: 'Order' }, { key: 'age', label: 'Age' }],
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
            entities: { kind: 'compareEntities', properties: ['throughput'] },
            metrics: [{ key: 'throughput', label: 'Throughput' }],
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
      relations: [{ predicate: 'has', label: 'Lines', relations: [{ predicate: 'for', label: 'Item' }] }],
    },
    translations: {
      es: {
        'Order Operations': 'Operaciones de pedidos',
        'Live view': 'Vista en vivo',
        site: 'sitio',
        Throughput: 'Rendimiento',
        'last hour': 'última hora',
        'Orders shipped': 'Pedidos enviados',
        goal: 'objetivo',
        'since midnight': 'desde medianoche',
        Fulfilment: 'Cumplimiento',
        'by stage': 'por etapa',
        orders: 'pedidos',
        Picked: 'Recogido',
        'ready to pack': 'listo para empacar',
        Order: 'Pedido',
        Backlog: 'Pendientes',
        'oldest first': 'más antiguos primero',
        Age: 'Antigüedad',
        Targets: 'Objetivos',
        'Fill rate': 'Tasa de cumplimiento',
        Sites: 'Sitios',
        Schedule: 'Horario',
        Morning: 'Mañana',
        Afternoon: 'Tarde',
        Problems: 'Problemas',
        'needs attention': 'requiere atención',
        Late: 'Retrasado',
        Details: 'Detalles',
        Lines: 'Líneas',
        Item: 'Artículo',
        // "Throughput" (metric label) shares the section-title translation above.
        // "Shipped" is intentionally absent: it is a state name / binding value,
        // never a translatable label.
      },
    },
  };
}

describe('localizeSpec', () => {
  it('renders display strings in the active locale', () => {
    const spec = localizeSpec(fixture(), 'es');
    expect(spec.title).toBe('Operaciones de pedidos');
    expect(spec.subtitle).toBe('Vista en vivo');
    expect(spec.compare?.label).toBe('sitio');

    const [throughput, exceptions] = spec.sections;
    expect(throughput.title).toBe('Rendimiento');
    expect(throughput.hint).toBe('última hora');

    const kpi = throughput.widgets[0] as KpiWidget;
    expect(kpi.title).toBe('Pedidos enviados');
    expect(kpi.targetLabel).toBe('objetivo');
    expect(kpi.footnote).toBe('desde medianoche');

    const funnel = throughput.widgets[1] as FunnelWidget;
    expect(funnel.title).toBe('Cumplimiento');
    expect(funnel.searchNoun).toBe('pedidos');
    expect(funnel.stages[0].label).toBe('Recogido');
    expect(funnel.stages[0].sublabel).toBe('listo para empacar');
    expect(funnel.drillColumns?.[0].label).toBe('Pedido');

    const table = throughput.widgets[2] as TableWidget;
    expect(table.columns.map((c) => c.label)).toEqual(['Pedido', 'Antigüedad']);

    const leaderboard = throughput.widgets[4] as LeaderboardWidget;
    expect(leaderboard.metrics[0].label).toBe('Rendimiento');

    expect(exceptions.widgets[0].title).toBe('Problemas');
    expect(spec.detail?.propertyGroups?.[0].label).toBe('Detalles');
    expect(spec.detail?.relations?.[0].label).toBe('Líneas');
    expect(spec.detail?.relations?.[0].relations?.[0].label).toBe('Artículo');
  });

  it('never translates binding values or model-vocabulary keys', () => {
    const spec = localizeSpec(fixture(), 'es');
    const kpi = spec.sections[0].widgets[0] as KpiWidget;
    // The state name stays "Shipped" even though the KPI unit "Shipped" was translatable.
    expect(kpi.value).toEqual({ kind: 'stateCount', state: 'Shipped' });
    // Row keys are identifiers, not labels — untouched.
    const table = spec.sections[0].widgets[2] as TableWidget;
    expect(table.columns.map((c) => c.key)).toEqual(['name', 'age']);
    expect(spec.detail?.relations?.[0].predicate).toBe('has');
  });

  it('falls back to the base text when the active locale is absent', () => {
    const spec = localizeSpec(fixture(), 'de');
    expect(spec.title).toBe('Order Operations');
    expect(spec.sections[0].title).toBe('Throughput');
  });

  it('falls back to the base text for a key missing within a present locale', () => {
    const spec = localizeSpec(fixture(), 'es');
    // The "Exceptions" section title has no es entry.
    expect(spec.sections[1].title).toBe('Exceptions');
  });

  it('returns a spec with no translations block unchanged (no regression)', () => {
    const base = fixture();
    delete base.translations;
    const spec = localizeSpec(base, 'es');
    expect(spec).toBe(base);
  });
});
