import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { Binding, BoxPlotWidget, BoxSeries } from '@tendril-ui/tendril';
import type { BindingResult, ResolveContext } from '@tendril-ui/tendril';

const values = new Map<string, BindingResult>();

vi.mock('@tendril-ui/tendril', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@tendril-ui/tendril')>()),
  useBindings: (bindings: (Binding | undefined)[]) =>
    bindings.map((binding) => ({
      loading: false,
      error: false,
      value: binding ? (values.get(JSON.stringify(binding)) ?? null) : null,
    })),
}));

const { BoxPlot } = await import('./BoxPlot');

const ONE_YEAR = 365 * 86_400;
const PERCENTILES = [5, 25, 50, 75, 95] as const;
const PARTS = ['whiskerLow', 'boxLow', 'median', 'boxHigh', 'whiskerHigh'] as const;

/** The reference parcel's hourly air temperature in 2025, in °C: the 5th, 25th, 50th, 75th and 95th
 *  percentiles of each month, January first, and of the whole year. */
const MONTHS = [
  [12.7, 16.6, 19.3, 22.4, 26.0], [14.1, 18.8, 21.1, 24.1, 31.1], [16.5, 20.4, 23.8, 27.9, 35.5],
  [20.8, 24.5, 28.7, 33.9, 43.2], [24.6, 27.6, 31.5, 37.2, 45.1], [26.8, 29.2, 32.9, 38.2, 44.6],
  [28.7, 32.2, 35.9, 40.0, 46.8], [30.4, 33.2, 36.2, 40.6, 45.5], [28.0, 30.2, 33.1, 37.8, 43.1],
  [23.9, 26.6, 29.8, 33.5, 38.1], [17.3, 20.9, 25.2, 29.0, 33.1], [14.0, 18.4, 21.4, 24.7, 30.2],
];
const YEAR = [16.7, 22.8, 28.9, 34.6, 43.9];

function series(
  fold: 'monthOfYear' | 'all', answers: (part: number) => { key: string; value: number }[], property = 'temperatureCelsius',
): BoxSeries {
  return Object.fromEntries(PARTS.map((part, at) => {
    const binding: Binding = {
      kind: 'history', property, windowSeconds: ONE_YEAR,
      steps: [{ fold, function: 'Percentile', percentile: PERCENTILES[at] }],
    };
    values.set(JSON.stringify(binding), answers(at));
    return [part, binding];
  })) as unknown as BoxSeries;
}

const months = series('monthOfYear', (part) => MONTHS.map((month, at) => ({ key: String(at + 1), value: month[part] })));
const annual = series('all', (part) => [{ key: 'all', value: YEAR[part] }]);

const widget: BoxPlotWidget = {
  type: 'boxPlot',
  title: 'Average monthly temperature (last year)',
  months,
  annual,
  format: 'decimal1',
  unit: '°C',
};

function draw(drawn: BoxPlotWidget = widget) {
  return render(<BoxPlot widget={drawn} context={{} as ResolveContext} />);
}

const top = (element: Element) => Number(element.getAttribute('y'));
const bottom = (element: Element) => top(element) + Number(element.getAttribute('height'));

describe('the box plot by month', () => {
  it('draws a box for each month and one for the whole window, each reachable from the keyboard', () => {
    const { container } = draw();

    expect(container.querySelectorAll('[data-month]')).toHaveLength(13);
    expect(container.querySelector('[data-month="annual"]')).not.toBeNull();
    expect(container.querySelector('[data-month="7"]')!.getAttribute('tabindex')).toBe('0');
  });

  it('draws the box between the quartiles, the median across it and the whiskers to the outer percentiles', () => {
    const { container } = draw();

    const july = container.querySelector('[data-month="7"]')!;
    const box = july.querySelector('[data-mark="box"]')!;
    const median = Number(july.querySelector('[data-mark="median"]')!.getAttribute('y1'));
    const whisker = july.querySelector('[data-mark="whisker"]')!;
    const whiskerTop = Number(whisker.getAttribute('y1'));
    const whiskerBottom = Number(whisker.getAttribute('y2'));

    expect(whiskerTop).toBeLessThan(top(box));
    expect(median).toBeGreaterThan(top(box));
    expect(median).toBeLessThan(bottom(box));
    expect(whiskerBottom).toBeGreaterThan(bottom(box));
    // July: box 32.2–40.0 °C, whiskers 28.7–46.8 °C, all on one linear scale.
    expect((bottom(box) - top(box)) / (whiskerBottom - whiskerTop)).toBeCloseTo((40.0 - 32.2) / (46.8 - 28.7), 5);
  });

  it('draws July higher than January', () => {
    const { container } = draw();

    const medianOf = (month: string) => Number(container.querySelector(`[data-month="${month}"] [data-mark="median"]`)!.getAttribute('y1'));
    expect(medianOf('7')).toBeLessThan(medianOf('1'));
  });

  it('draws each band behind the boxes between the bounds the model states', () => {
    const from: Binding = { kind: 'const', value: 28 };
    const to: Binding = { kind: 'const', value: 31 };
    values.set(JSON.stringify(from), 28);
    values.set(JSON.stringify(to), 31);
    const { container } = draw({ ...widget, bands: [{ label: 'high', from, to, colour: '#f97316' }] });

    const band = container.querySelector('rect[data-band="high"]')!;
    expect(band.getAttribute('fill')).toBe('#f97316');
    expect(Number(band.getAttribute('height'))).toBeGreaterThan(0);
    expect(screen.getByText('high')).toBeInTheDocument();
  });

  it('reads all five figures of a box on focus', () => {
    draw();

    fireEvent.focus(screen.getByRole('img', { name: /^Jul/ }));

    const tooltip = within(screen.getByRole('tooltip'));
    expect(tooltip.getByText('46.8 °C')).toBeInTheDocument();
    expect(tooltip.getByText('35.9 °C')).toBeInTheDocument();
    expect(tooltip.getByText('28.7 °C')).toBeInTheDocument();
  });

  it('carries the window, a legend of the marks and a table twin of every box', () => {
    draw();

    expect(screen.getByText('last year')).toBeInTheDocument();
    const rows = within(screen.getByRole('table')).getAllByRole('row');
    expect(rows).toHaveLength(14);
    expect(rows[13].textContent).toBe('Annual16.7 °C22.8 °C28.9 °C34.6 °C43.9 °C');
  });

  it('leaves out a month the platform answered nothing for, and the annual box where none is bound', () => {
    const summer = series('monthOfYear', (part) => [6, 7, 8].map((month) => ({ key: String(month), value: MONTHS[month - 1][part] })), 'summerOnly');
    const { container } = draw({ ...widget, months: summer, annual: undefined });

    expect([...container.querySelectorAll('[data-month]')].map((month) => month.getAttribute('data-month'))).toEqual(['6', '7', '8']);
  });
});
