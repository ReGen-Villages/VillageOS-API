import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { Binding, DivergingBarWidget } from '../../../types/dashboard';
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

const { DivergingBar } = await import('./DivergingBar');

const A_YEAR = 365 * 86_400;

/** Degree days per month: cooling grows in summer, heating in winter. */
function degreeDays(threshold: number, direction: 'SumAbove' | 'SumBelow', of: (month: number) => number): Binding {
  const binding: Binding = {
    kind: 'history', property: 'temperatureCelsius', windowSeconds: A_YEAR,
    steps: [{ fold: 'day', function: 'Average' }, { fold: 'monthOfYear', function: direction, threshold }],
  };
  values.set(JSON.stringify(binding), Array.from({ length: 12 }, (_, at) => ({ key: String(at + 1), value: of(at + 1) })));
  return binding;
}

const cooling = (month: number) => Math.max(0, 120 * Math.cos(((month - 7) / 12) * 2 * Math.PI));
const heating = (month: number) => Math.max(0, 300 * Math.cos(((month - 1) / 12) * 2 * Math.PI));

const coolingSetpoint: Binding = { kind: 'property', thing: '$scope', property: 'coolingSetpointCelsius' };
const heatingSetpoint: Binding = { kind: 'property', thing: '$scope', property: 'heatingSetpointCelsius' };
values.set(JSON.stringify(coolingSetpoint), 18);
values.set(JSON.stringify(heatingSetpoint), 10);

const widget: DivergingBarWidget = {
  type: 'divergingBar',
  title: 'Degree days',
  unit: '°C·d',
  format: 'integer',
  up: { label: 'Cooling', value: degreeDays(18, 'SumAbove', cooling), threshold: coolingSetpoint },
  down: { label: 'Heating', value: degreeDays(10, 'SumBelow', heating), threshold: heatingSetpoint },
};

function draw(drawn: DivergingBarWidget = widget) {
  return render(<DivergingBar widget={drawn} ctx={{} as ResolveContext} />);
}

describe('the diverging bar', () => {
  it('draws each month as a bar rising above the line and one falling below it, sized to the figures', () => {
    const { container } = draw();

    expect(container.querySelectorAll('[data-month]')).toHaveLength(12);
    const july = container.querySelector('[data-month="7"]')!;
    const up = july.querySelector('rect[data-direction="up"]')!;
    const down = july.querySelector('rect[data-direction="down"]')!;
    const baseline = Number(container.querySelector('line[data-mark="baseline"]')!.getAttribute('y1'));
    expect(Number(up.getAttribute('y')) + Number(up.getAttribute('height'))).toBeCloseTo(baseline, 5);
    expect(Number(down.getAttribute('y'))).toBeCloseTo(baseline, 5);
    expect(Number(up.getAttribute('height'))).toBeGreaterThan(0);
    expect(Number(down.getAttribute('height'))).toBe(0);
  });

  it('draws both directions on one scale, so the taller figure is the taller bar', () => {
    const { container } = draw();

    const januaryDown = container.querySelector('[data-month="1"] rect[data-direction="down"]')!;
    const julyUp = container.querySelector('[data-month="7"] rect[data-direction="up"]')!;
    expect(Number(januaryDown.getAttribute('height')) / Number(julyUp.getAttribute('height'))).toBeCloseTo(300 / 120, 5);
  });

  it('names each direction with the threshold the model states', () => {
    draw();

    const legend = screen.getByRole('list');
    expect(within(legend).getByText(/Cooling/).textContent).toContain('18');
    expect(within(legend).getByText(/Heating/).textContent).toContain('10');
  });

  it('reads both figures of a month on focus', () => {
    draw();

    fireEvent.focus(screen.getByRole('img', { name: /^Jan/ }));

    const tooltip = screen.getByRole('tooltip');
    expect(within(tooltip).getByText('300 °C·d')).toBeInTheDocument();
    expect(within(tooltip).getByText('0 °C·d')).toBeInTheDocument();
  });

  it('opens the readout for the bar under the pointer and closes it when the pointer or the focus leaves', () => {
    draw();

    fireEvent.mouseEnter(screen.getByRole('img', { name: /^Jul/ }));
    expect(within(screen.getByRole('tooltip')).getByText('120 °C·d')).toBeInTheDocument();

    fireEvent.mouseLeave(screen.getByRole('img', { name: /^Jul/ }));
    expect(screen.queryByRole('tooltip')).toBeNull();

    fireEvent.focus(screen.getByRole('img', { name: /^Jul/ }));
    fireEvent.blur(screen.getByRole('img', { name: /^Jul/ }));
    expect(screen.queryByRole('tooltip')).toBeNull();
  });

  it('carries the window and a table twin of every month', () => {
    draw();

    expect(screen.getByText('last year')).toBeInTheDocument();
    expect(within(screen.getByRole('table')).getAllByRole('row')).toHaveLength(13);
  });

  it('leaves out a month neither direction was answered for, and names a side with no threshold bound alone', () => {
    const only = degreeDays(18, 'SumAbove', () => 1);
    values.set(JSON.stringify(only), [{ key: '6', value: 40 }, { key: '7', value: 55 }]);
    const { container } = draw({ ...widget, up: { ...widget.up, value: only }, down: { label: 'Heating', value: { kind: 'const', value: 0 } } });

    expect(container.querySelectorAll('[data-month]')).toHaveLength(2);
    expect(within(screen.getByRole('list')).getByText('Heating')).toBeInTheDocument();
  });
});
