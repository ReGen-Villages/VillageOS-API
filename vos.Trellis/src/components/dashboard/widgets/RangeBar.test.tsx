import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { Binding, RangeBarWidget, RangeSeries } from '../../../types/dashboard';
import type { HistoryStep } from '../../../types/vos';
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

const { RangeBar } = await import('./RangeBar');

const A_YEAR = 31_536_000;

/** The seven statistics of one month, in the order the bar stacks them, top to bottom. */
type Seven = [number, number, number, number, number, number, number];

const MONTHS: Seven[] = [
  [33, 32, 27, 22, 17, 15, 14], [33, 31.8, 25.8, 21.3, 17.5, 15.2, 14.5], [29, 27.8, 24, 19.4, 15.6, 12.5, 12],
  [25, 24, 21, 16.6, 13, 10, 9], [24, 22.9, 19.8, 14, 9, 5.5, 5], [21.6, 20.5, 17.9, 11.5, 6.4, 2.5, 2],
  [21.8, 20.4, 17.7, 11.9, 6.6, 3.5, 3], [30.6, 28.5, 22.2, 15.9, 10, 5.3, 4.5], [30.7, 29.5, 26.1, 20, 14, 11.5, 11],
  [32, 31, 25, 19, 13.8, 10, 9.7], [28.5, 26.9, 22, 17.8, 14, 11.2, 10.8], [28, 27.5, 24.4, 19.6, 15.2, 13, 12.6],
];
const ANNUAL: Seven = [33, 30.3, 22.7, 17.3, 12.6, 4.7, 2];

const STATISTICS: (keyof RangeSeries)[] = [
  'recordedHigh', 'designHigh', 'averageHigh', 'mean', 'averageLow', 'designLow', 'recordedLow',
];

/** The seven questions the contract worked out, one per statistic, folded by month or over all. */
function stepsFor(statistic: keyof RangeSeries, fold: 'monthOfYear' | 'all'): HistoryStep[] {
  switch (statistic) {
    case 'recordedHigh': return [{ fold, function: 'Max' }];
    case 'designHigh': return [{ fold, function: 'Percentile', percentile: 99.5 }];
    case 'averageHigh': return [{ fold: 'day', function: 'Max' }, { fold, function: 'Average' }];
    case 'mean': return [{ fold, function: 'Average' }];
    case 'averageLow': return [{ fold: 'day', function: 'Min' }, { fold, function: 'Average' }];
    case 'designLow': return [{ fold, function: 'Percentile', percentile: 0.5 }];
    case 'recordedLow': return [{ fold, function: 'Min' }];
  }
}

function series(name: string, months: Seven[] | null, annual: Seven | null): RangeSeries {
  const bound = {} as RangeSeries;
  STATISTICS.forEach((statistic, at) => {
    const binding: Binding = {
      kind: 'history', property: name, windowSeconds: A_YEAR, steps: stepsFor(statistic, months ? 'monthOfYear' : 'all'),
    };
    const rows = months
      ? months.map((month, index) => ({ key: String(index + 1), value: month[at] }))
      : [{ key: 'all', value: annual![at] }];
    values.set(JSON.stringify(binding), rows);
    bound[statistic] = binding;
  });
  return bound;
}

function bound(name: string, value: number): Binding {
  const binding: Binding = { kind: 'property', thing: '$scope', property: name };
  values.set(JSON.stringify(binding), value);
  return binding;
}

const widget: RangeBarWidget = {
  type: 'rangeBar',
  title: 'Temperature range',
  months: series('temperature', MONTHS, null),
  annual: series('temperature', null, ANNUAL),
  bands: [
    { label: 'Comfort', from: bound('comfortLowCelsius', 20), to: bound('comfortHighCelsius', 26.5), colour: '#d0d0d0' },
    { label: 'Fatal', from: bound('wetBulbFatalCelsius', 35), colour: '#dc2626' },
  ],
  unit: '°C',
  format: 'decimal1',
  floor: -10,
  ceiling: 40,
};

function draw(drawn: RangeBarWidget = widget) {
  return render(<RangeBar widget={drawn} context={{} as ResolveContext} />);
}

describe('the stacked range bar', () => {
  it('draws a bar for each month and one for the whole window, each reachable from the keyboard', () => {
    draw();

    const bars = screen.getAllByRole('img', { name: /Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec|Annual/ });
    expect(bars).toHaveLength(13);
    expect(bars.map((bar) => bar.getAttribute('tabindex'))).toEqual(Array(13).fill('0'));
    expect(bars[12]).toHaveAccessibleName(/Annual/);
  });

  it('stacks four segments from the design low to the design high with the mean as a line and the record as open circles', () => {
    const { container } = draw();

    const january = container.querySelector('[data-month="1"]')!;
    const segments = [...january.querySelectorAll('rect[data-segment]')].map((rectangle) => ({
      name: rectangle.getAttribute('data-segment'),
      top: Number(rectangle.getAttribute('y')),
      height: Number(rectangle.getAttribute('height')),
    }));
    expect(segments.map((segment) => segment.name)).toEqual(['designHigh', 'averageHigh', 'averageLow', 'designLow']);
    // Stacked top to bottom without overlap, and a taller segment for a wider span.
    for (let at = 1; at < segments.length; at++) {
      expect(segments[at].top).toBeGreaterThanOrEqual(segments[at - 1].top + segments[at - 1].height);
    }
    // Design high spans 5 degrees and average low 5 degrees, average high 5 degrees: equal heights.
    expect(segments[0].height).toBeCloseTo(segments[1].height, 5);
    expect(segments[3].height).toBeCloseTo(segments[2].height * (2 / 5), 5);

    expect(january.querySelector('line[data-mark="mean"]')).not.toBeNull();
    expect(january.querySelectorAll('circle[data-mark]')).toHaveLength(2);
  });

  it('draws each band behind the bars between the bounds the model states, open-ended to the edge', () => {
    const { container } = draw();

    const bands = [...container.querySelectorAll('rect[data-band]')];
    expect(bands.map((band) => band.getAttribute('data-band'))).toEqual(['Comfort', 'Fatal']);
    expect(bands[0]).toHaveAttribute('fill', '#d0d0d0');
    const comfort = Number(bands[0].getAttribute('height'));
    const fatal = Number(bands[1].getAttribute('height'));
    // 6.5 degrees against the 5 between 35 and the 40 ceiling.
    expect(comfort / fatal).toBeCloseTo(6.5 / 5, 5);
    expect(screen.getByText('Comfort')).toBeInTheDocument();
  });

  it('labels the axis in the spec\'s floor and ceiling and the widget\'s unit', () => {
    draw();

    expect(screen.getByText('-10')).toBeInTheDocument();
    expect(screen.getByText('40')).toBeInTheDocument();
    expect(screen.getAllByText('°C').length).toBeGreaterThan(0);
  });

  it('says which window it draws, read off the bindings', () => {
    draw();

    expect(screen.getByText('last year')).toBeInTheDocument();
  });

  it('shows every statistic of a bar on focus, and lets the keyboard read them all', () => {
    draw();

    fireEvent.focus(screen.getByRole('img', { name: /^Mar/ }));

    const tooltip = screen.getByRole('tooltip');
    expect(within(tooltip).getByText(/Mar/)).toBeInTheDocument();
    expect(within(tooltip).getByText('27.8 °C')).toBeInTheDocument();
    expect(within(tooltip).getByText('Design high')).toBeInTheDocument();
  });

  it('opens the tooltip on the other side of a bar near the right edge, so it stays inside the card', () => {
    draw();

    fireEvent.mouseEnter(screen.getByRole('img', { name: /^Dec/ }));

    expect(screen.getByRole('tooltip').style.right).not.toBe('');
    expect(screen.getByRole('tooltip').style.left).toBe('');
  });

  it('reads a window shorter than a year in days', () => {
    const ninetyDays = 90 * 86_400;
    const months = series('rain', MONTHS, null);
    const mean: Binding = { ...months.mean, windowSeconds: ninetyDays } as Binding;
    values.set(JSON.stringify(mean), values.get(JSON.stringify(months.mean))!);
    draw({ ...widget, annual: undefined, months: { ...months, mean } });

    expect(screen.getByText('last 90 days')).toBeInTheDocument();
  });

  it('carries a legend for the seven statistics and a table twin of every bar', () => {
    draw();

    const legend = screen.getByRole('list');
    expect(within(legend).getByText('Recorded high')).toBeInTheDocument();
    expect(within(legend).getByText('Recorded low')).toBeInTheDocument();
    const table = screen.getByRole('table');
    expect(within(table).getAllByRole('row')).toHaveLength(14);
    expect(within(table).getByText('30.3 °C')).toBeInTheDocument();
  });

  it('leaves out a month the platform answered nothing for, and the annual bar where none is bound', () => {
    const { annual: _none, ...withoutAnnual } = widget;
    const eleven = MONTHS.slice(0, 11);
    draw({ ...withoutAnnual, months: series('humidity', eleven, null) });

    expect(screen.getAllByRole('img', { name: /Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec/ })).toHaveLength(11);
    expect(screen.queryByRole('img', { name: /Annual/ })).toBeNull();
  });
});
