import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { Binding, SmallMultiplesWidget } from '../../../types/dashboard';
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

const { SmallMultiples } = await import('./SmallMultiples');

const TEN_YEARS = 10 * 365 * 86_400;

/** One figure per month and hour, keyed the way the platform keys a composite fold: "month,hour". */
function byMonthAndHour(property: string, of: (month: number, hour: number) => number): Binding {
  const binding: Binding = {
    kind: 'history', property, windowSeconds: TEN_YEARS,
    steps: [{ fold: 'monthOfYear,hourOfDay', function: 'Average' }],
  };
  const rows = [];
  for (let month = 1; month <= 12; month++)
    for (let hour = 0; hour < 24; hour++) rows.push({ key: `${month},${hour}`, value: of(month, hour) });
  values.set(JSON.stringify(binding), rows);
  return binding;
}

const humidity = (month: number, hour: number) => 60 + 20 * Math.cos((hour / 24) * 2 * Math.PI) + (month % 3);
const temperature = (month: number, hour: number) => 10 + month - 10 * Math.cos((hour / 24) * 2 * Math.PI);

const comfortLow: Binding = { kind: 'property', thing: '$scope', property: 'comfortLowCelsius' };
const comfortHigh: Binding = { kind: 'property', thing: '$scope', property: 'comfortHighCelsius' };
values.set(JSON.stringify(comfortLow), 20);
values.set(JSON.stringify(comfortHigh), 26.5);

const widget: SmallMultiplesWidget = {
  type: 'smallMultiples',
  title: 'Humidity and temperature through the day',
  bars: { label: 'Relative humidity', unit: '%', format: 'integer', value: byMonthAndHour('relativeHumidityPercent', humidity) },
  line: { label: 'Temperature', unit: '°C', format: 'decimal1', value: byMonthAndHour('temperatureCelsius', temperature) },
  band: { label: 'Comfort band', colour: '#9ca3af', from: comfortLow, to: comfortHigh },
};

function draw(drawn: SmallMultiplesWidget = widget) {
  return render(<SmallMultiples widget={drawn} context={{} as ResolveContext} />);
}

describe('the small multiples', () => {
  it('draws twelve panels, each with a bar an hour and one line through the hours', () => {
    const { container } = draw();

    const panels = container.querySelectorAll('[data-month]');
    expect(panels).toHaveLength(12);
    expect(panels[0].querySelectorAll('rect[data-hour]')).toHaveLength(24);
    expect(panels[0].querySelector('path[data-series="line"]')!.getAttribute('d')!.split('L')).toHaveLength(24);
  });

  // One scale for the bars across every panel, so a month with more of the figure has taller bars than
  // a month with less; the tallest bar anywhere fills its panel.
  it('draws the bars on one scale across the panels, the tallest anywhere filling its panel', () => {
    const { container } = draw();

    const heights = [...container.querySelectorAll('rect[data-hour]')].map((bar) => Number(bar.getAttribute('height')));
    const panelHeight = Number(container.querySelector('[data-month="1"]')!.getAttribute('data-plot-height'));
    expect(Math.max(...heights)).toBeCloseTo(panelHeight, 5);
    const july = [...container.querySelectorAll('[data-month="7"] rect[data-hour]')].map((bar) => Number(bar.getAttribute('height')));
    expect(Math.max(...july)).toBeLessThan(panelHeight);
  });

  it('draws the band behind the line between the bounds the model states, on the line scale', () => {
    const { container } = draw();

    const band = container.querySelector('[data-month="1"] rect[data-band]')!;
    expect(band).not.toBeNull();
    expect(Number(band.getAttribute('height'))).toBeGreaterThan(0);
  });

  it('reads both figures of an hour on focus', () => {
    draw();

    fireEvent.focus(screen.getByRole('img', { name: /^Jan/ }));
    fireEvent.keyDown(screen.getByRole('img', { name: /^Jan/ }), { key: 'ArrowRight' });

    const tooltip = screen.getByRole('tooltip');
    expect(within(tooltip).getByText(/%$/)).toBeInTheDocument();
    expect(within(tooltip).getByText(/°C$/)).toBeInTheDocument();
  });

  it('follows the pointer from bar to bar, walks the hours with the keys, and closes when the pointer or the focus leaves', () => {
    const { container } = draw();
    const march = screen.getByRole('img', { name: /^Mar/ });

    fireEvent.mouseEnter(container.querySelector('[data-month="3"] rect[data-hour="5"]')!);
    expect(screen.getByRole('tooltip').textContent).toContain('05:00');

    fireEvent.mouseLeave(march);
    expect(screen.queryByRole('tooltip')).toBeNull();

    fireEvent.focus(march);
    fireEvent.keyDown(march, { key: 'End' });
    expect(screen.getByRole('tooltip').textContent).toContain('23:00');
    fireEvent.keyDown(march, { key: 'ArrowRight' });
    expect(screen.getByRole('tooltip').textContent).toContain('00:00');
    fireEvent.keyDown(march, { key: 'ArrowLeft' });
    fireEvent.keyDown(march, { key: 'Home' });
    expect(screen.getByRole('tooltip').textContent).toContain('00:00');
    fireEvent.keyDown(march, { key: 'a' });
    fireEvent.blur(march);
    expect(screen.queryByRole('tooltip')).toBeNull();
  });

  it('carries the window, a legend naming both series and the band, and a table twin of every hour of every month', () => {
    draw();

    expect(screen.getByText('last 10 years')).toBeInTheDocument();
    const legend = screen.getByRole('list');
    expect(within(legend).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Relative humidity', 'Temperature', 'Comfort band']);
    // Counted off the table itself: a role query over every row of every hour walks the whole tree per row.
    expect(screen.getByRole('table').querySelectorAll('tr')).toHaveLength(12 * 24 + 1);
  });

  it('leaves out a month neither series was answered for', () => {
    const only: Binding = { kind: 'history', property: 'x', windowSeconds: TEN_YEARS, steps: [{ fold: 'monthOfYear,hourOfDay', function: 'Average' }] };
    values.set(JSON.stringify(only), [{ key: '6,0', value: 1 }, { key: '6,1', value: 2 }]);
    const { container } = draw({ ...widget, bars: { ...widget.bars, value: only }, line: { ...widget.line, value: { kind: 'const', value: 0 } }, band: undefined });

    expect(container.querySelectorAll('[data-month]')).toHaveLength(1);
    expect(container.querySelector('rect[data-band]')).toBeNull();
  });
});
