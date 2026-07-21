import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { Binding, KpiWidget } from '../../../types/dashboard';
import type { BindingResult, ResolveContext } from '../../../api/dashboardApi';

const values = new Map<string, BindingResult>();

vi.mock('../../../hooks/useDashboard', () => ({
  useBinding: (binding: Binding | undefined) => ({
    loading: false,
    error: false,
    value: binding ? (values.get(JSON.stringify(binding)) ?? null) : null,
  }),
}));

const { KpiCard } = await import('./KpiCard');

function bind(name: string, value: BindingResult): Binding {
  const binding = { kind: 'property', thing: name, property: name } as unknown as Binding;
  values.set(JSON.stringify(binding), value);
  return binding;
}

describe('KpiCard trace summary', () => {
  it('reports the peak and trough of the series it is drawing', () => {
    const widget: KpiWidget = {
      type: 'kpi',
      title: 'Yield',
      format: 'integer',
      unit: 'u/hr',
      value: bind('yield_per_hour', 240),
      spark: bind('series', [120, 480, 300, 96]),
      sparkBaseline: bind('average_yield_per_hour', 210),
      sparkBaselineLabel: '12-hour average',
    };

    render(<KpiCard widget={widget} ctx={{} as ResolveContext} />);

    expect(screen.getByText(/peak 480 · trough 96/)).toBeInTheDocument();
    expect(screen.getByText(/12-hour average 210/)).toBeInTheDocument();
  });

  it('leaves the summary out when there is no series to summarise', () => {
    const widget: KpiWidget = {
      type: 'kpi',
      title: 'Forecast accuracy',
      format: 'decimal1',
      value: bind('forecast_accuracy', 99.4),
    };

    render(<KpiCard widget={widget} ctx={{} as ResolveContext} />);

    expect(screen.queryByText(/peak/)).toBeNull();
  });
});
