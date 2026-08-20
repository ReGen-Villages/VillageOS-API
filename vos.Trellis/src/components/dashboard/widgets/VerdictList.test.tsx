import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { Binding, VerdictWidget } from '../../../types/dashboard';
import type { BindingResult, ResolveContext } from '../../../api/dashboardApi';

const values = new Map<string, BindingResult>();

vi.mock('../../../hooks/useDashboard', () => ({
  useBindings: (bindings: (Binding | undefined)[]) =>
    bindings.map((binding) => ({
      loading: false,
      error: false,
      value: binding ? (values.get(JSON.stringify(binding)) ?? null) : null,
    })),
}));

const { VerdictList } = await import('./VerdictList');

let bindingCount = 0;
function bind(value: BindingResult): Binding {
  const binding = { kind: 'verdict', thing: `study-${bindingCount++}`, states: [] } as unknown as Binding;
  values.set(JSON.stringify(binding), value);
  return binding;
}

function widgetWith(...rows: VerdictWidget['rows']): VerdictWidget {
  return { type: 'verdict', title: 'Balances', rows };
}

function draw(widget: VerdictWidget) {
  render(<VerdictList widget={widget} ctx={{} as ResolveContext} />);
}

describe('VerdictList', () => {
  it('reads a balance that clears as a sentence naming the target it was judged against', () => {
    draw(widgetWith({
      label: 'Energy',
      format: 'pct100',
      verdicts: bind([{
        state: 'EnergyNetPositive',
        reads: '{value} of assumed consumption — meets the {target} target',
        property: 'pctOfConsumption',
        operator: '>=',
        target: 100,
        value: 112,
      }]),
    }));

    expect(screen.getByText('Energy')).toBeInTheDocument();
    expect(screen.getByText('112% of assumed consumption — meets the 100% target')).toBeInTheDocument();
  });

  it('reads a balance that falls short as short of its target', () => {
    draw(widgetWith({
      label: 'Energy',
      format: 'pct100',
      verdicts: bind([{
        state: 'EnergyShortOfTarget',
        reads: '{value} of assumed consumption — short of the {target} target',
        property: 'pctOfConsumption',
        operator: '<',
        target: 100,
        value: 73,
      }]),
    }));

    expect(screen.getByText('73% of assumed consumption — short of the 100% target')).toBeInTheDocument();
  });

  // A site nobody assessed must not read as a site that failed.
  it('reads a withheld verdict in its own words, with no figure and no zero', () => {
    draw(widgetWith({
      label: 'Water',
      format: 'decimal1',
      unit: 'days',
      verdicts: bind([{
        state: 'WaterNotAssessed',
        reads: 'not assessed — no rainfall figure was resolved for this site',
        property: null,
        operator: null,
        target: null,
        value: null,
      }]),
    }));

    expect(screen.getByText('not assessed — no rainfall figure was resolved for this site')).toBeInTheDocument();
    expect(screen.queryByText(/\b0\b/)).not.toBeInTheDocument();
  });

  // Ranges are independent criteria; a single-valued row would hide that several hold.
  it('reads every verdict a balance holds, not just the first', () => {
    draw(widgetWith({
      label: 'Energy',
      format: 'pct100',
      verdicts: bind([
        { state: 'EnergyNetPositive', reads: 'meets the {target} target', property: 'pctOfConsumption', operator: '>=', target: 100, value: 100 },
        { state: 'EnergyShortOfTarget', reads: 'short of the {target} target', property: 'pctOfConsumption', operator: '<', target: 100, value: 100 },
      ]),
    }));

    expect(screen.getByText('meets the 100% target')).toBeInTheDocument();
    expect(screen.getByText('short of the 100% target')).toBeInTheDocument();
  });

  it('says nothing about a balance the model returned no verdict for', () => {
    draw(widgetWith({ label: 'Food', verdicts: bind([]) }));

    expect(screen.getByText('Food')).toBeInTheDocument();
    expect(screen.queryByText('—')).not.toBeInTheDocument();
  });

  it('reads each balance of a several-row widget under its own label', () => {
    draw(widgetWith(
      {
        label: 'Energy',
        format: 'pct100',
        verdicts: bind([{ state: 'EnergyShortOfTarget', reads: 'short of the {target} target', property: 'pctOfConsumption', operator: '<', target: 100, value: 73 }]),
      },
      {
        label: 'Water',
        format: 'decimal1',
        unit: 'days',
        verdicts: bind([{ state: 'WaterResilient', reads: '{value} of supply — clears the {target} target', property: 'daysOfSupply', operator: '>=', target: 14, value: 19.9 }]),
      },
    ));

    expect(screen.getByText('short of the 100% target')).toBeInTheDocument();
    expect(screen.getByText('19.9 days of supply — clears the 14.0 days target')).toBeInTheDocument();
  });
});
