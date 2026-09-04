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

// Story #6475: a figure a reader cannot place is a figure they have to trust.
describe('KpiCard says where the figure came from', () => {
  function bindOrigin(name: string, rows: BindingResult): Binding {
    const binding = { kind: 'origin', property: name, reads: {} } as unknown as Binding;
    values.set(JSON.stringify(binding), rows);
    return binding;
  }

  function draw(rows: BindingResult) {
    const widget: KpiWidget = {
      type: 'kpi',
      title: 'Rainfall',
      format: 'integer',
      value: bind('rainfall', 700),
      origin: bindOrigin('rainfall', rows),
    };
    return render(<KpiCard widget={widget} ctx={{} as ResolveContext} />);
  }

  it('reads a fetched figure in the model\'s words, naming its source and instant', () => {
    draw([{ origin: 'measured', reads: 'resolved {resolvedAt} from {source}', source: 'Open rainfall archive', resolvedAt: '2026-08-19' }]);
    expect(screen.getByText('resolved 2026-08-19 from Open rainfall archive')).toBeInTheDocument();
  });

  // Wording alone would leave the two alike at a glance, and a glance is what a tile is read with.
  it('draws a stated figure and a fetched one in different tones', () => {
    const { unmount } = draw([{ origin: 'stated', reads: 'as submitted', source: null, resolvedAt: null }]);
    const stated = screen.getByText('as submitted').className;
    unmount();

    draw([{ origin: 'measured', reads: 'measured on site', source: null, resolvedAt: null }]);
    expect(screen.getByText('measured on site').className).not.toBe(stated);
  });

  it('says an unrecorded origin is unrecorded rather than saying nothing', () => {
    draw([{ origin: 'unknown', reads: 'origin not recorded', source: null, resolvedAt: null }]);
    expect(screen.getByText('origin not recorded')).toBeInTheDocument();
  });

  // What a slot bound to something other than an origin binding resolves to. Its wording answers a
  // different question, and drawn here it would read as a claim about where the figure came from.
  it('draws no line for a row carrying no origin', () => {
    draw([{ state: 'EnergyNetPositive', reads: 'meets the target', value: 112, target: 100 }]);
    expect(screen.queryByText('meets the target')).toBeNull();
  });

  it('draws no line where the model gave no wording', () => {
    const { container } = draw([{ origin: 'unknown', reads: null, source: null, resolvedAt: null }]);
    expect(container.textContent).not.toContain('null');
    expect(screen.queryByText('undefined')).toBeNull();
  });

  it('says it of each Thing a binding reaching several reports', () => {
    draw([
      { origin: 'stated', reads: 'as submitted', source: null, resolvedAt: null },
      { origin: 'assumed', reads: 'assumed from {source}', source: 'Site', resolvedAt: null },
    ]);
    expect(screen.getByText('as submitted')).toBeInTheDocument();
    expect(screen.getByText('assumed from Site')).toBeInTheDocument();
  });

  it('draws nothing when the widget declares no origin', () => {
    const widget: KpiWidget = { type: 'kpi', title: 'Rainfall', value: bind('rainfall', 700) };
    const { container } = render(<KpiCard widget={widget} ctx={{} as ResolveContext} />);
    expect(container.querySelectorAll('div').length).toBeGreaterThan(0);
    expect(screen.queryByText(/as submitted/)).toBeNull();
  });
});

// A widget saying neither direction is good is saying there is no verdict to reach — the same for the
// badge against a target as for the tone on the delta. Judging it as though rising were good is the
// answer that reads as the opposite of what the figure means.
describe('KpiCard where the widget calls neither direction good', () => {
  const measuredAgainstStated = (direction: KpiWidget['direction']): KpiWidget => ({
    type: 'kpi',
    title: 'Measured area',
    value: bind('measured_area', 84),
    target: 100,
    direction,
  });

  it('reaches no verdict against a target', () => {
    render(<KpiCard widget={measuredAgainstStated('neither-good')} ctx={{} as ResolveContext} />);

    expect(screen.queryByText('on target')).toBeNull();
    expect(screen.queryByText('watch')).toBeNull();
  });

  it('still reaches one where the widget names a direction', () => {
    render(<KpiCard widget={measuredAgainstStated('up-good')} ctx={{} as ResolveContext} />);

    expect(screen.getByText('watch')).toBeInTheDocument();
  });
});
