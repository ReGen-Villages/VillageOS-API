import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { Binding, LineSeriesWidget } from '../../../types/dashboard';
import type { BindingResult, ResolveContext } from '../../../api/dashboardApi';
import type { HistoryStep } from '../../../types/vos';

const values = new Map<string, BindingResult>();

vi.mock('../../../hooks/useDashboard', () => ({
  useBindings: (bindings: (Binding | undefined)[]) =>
    bindings.map((binding) => ({
      loading: false,
      error: false,
      value: binding ? (values.get(JSON.stringify(binding)) ?? null) : null,
    })),
}));

const { LineSeries } = await import('./LineSeries');

const ELEVEN_YEARS = 11 * 365 * 86_400;

/** Monthly figures from January 2015 to December 2025, keyed as the platform keys a month fold. */
function monthly(offset: number, swing: number): { key: string; value: number }[] {
  const rows: { key: string; value: number }[] = [];
  for (let year = 2015; year <= 2025; year++) {
    for (let month = 1; month <= 12; month++) {
      const season = Math.cos(((month - 1) / 12) * 2 * Math.PI);
      rows.push({ key: `${year}-${String(month).padStart(2, '0')}`, value: offset + swing * season });
    }
  }
  return rows;
}

function history(name: string, steps: HistoryStep[], rows: BindingResult): Binding {
  const binding: Binding = { kind: 'history', property: name, windowSeconds: ELEVEN_YEARS, steps };
  values.set(JSON.stringify(binding), rows);
  return binding;
}

const widget: LineSeriesWidget = {
  type: 'lineSeries',
  title: 'Average monthly temperature',
  series: [
    { label: 'Daily high', value: history('temperature', [{ fold: 'day', function: 'Max' }, { fold: 'month', function: 'Average' }], monthly(24, 5)) },
    { label: 'Daily mean', value: history('temperature', [{ fold: 'month', function: 'Average' }], monthly(17, 5)) },
    { label: 'Daily low', value: history('temperature', [{ fold: 'day', function: 'Min' }, { fold: 'month', function: 'Average' }], monthly(11, 5)) },
  ],
  unit: '°C',
  format: 'decimal1',
};

function draw(drawn: LineSeriesWidget = widget) {
  return render(<LineSeries widget={drawn} ctx={{} as ResolveContext} />);
}

describe('the multi-line series', () => {
  it('draws one line with a point per group for each series, in the palette order', () => {
    const { container } = draw();

    const lines = [...container.querySelectorAll('path[data-series]')];
    expect(lines.map((line) => line.getAttribute('data-series'))).toEqual(['Daily high', 'Daily mean', 'Daily low']);
    expect(lines.map((line) => line.getAttribute('stroke'))).toEqual(['var(--series-1)', 'var(--series-2)', 'var(--series-3)']);
    expect(container.querySelectorAll('circle[data-series="Daily mean"]')).toHaveLength(132);
  });

  it('labels the years along the bottom, once each, at the first month of each', () => {
    draw();

    for (const year of ['2015', '2020', '2025']) expect(screen.getByText(year)).toBeInTheDocument();
    expect(screen.queryByText('2014')).toBeNull();
  });

  it('carries a legend for the series, the window it draws, and a table twin of every group', () => {
    const { container } = draw();

    const legend = screen.getByRole('list');
    expect(within(legend).getAllByRole('listitem').map((item) => item.textContent)).toEqual([
      'Daily high', 'Daily mean', 'Daily low',
    ]);
    expect(screen.getByText('last 11 years')).toBeInTheDocument();
    // Counted off the DOM rather than by role: a role query walks every node of the 133-row table,
    // which took the loaded build agent past the test timeout.
    expect(container.querySelectorAll('table tr')).toHaveLength(133);
  });

  it('opens a readout of every series at the group nearest the pointer', () => {
    const { container } = draw();
    const chart = container.querySelector('svg')!;
    Object.defineProperty(chart, 'getBoundingClientRect', { value: () => ({ left: 0, top: 0, width: 480, height: 260 }) });

    fireEvent.mouseMove(chart, { clientX: 479, clientY: 100 });

    const tooltip = screen.getByRole('tooltip');
    expect(within(tooltip).getByText('2025-12')).toBeInTheDocument();
    expect(within(tooltip).getByText('Daily high')).toBeInTheDocument();
    expect(within(tooltip).getAllByText(/°C/)).toHaveLength(3);
  });

  it('walks the groups from the keyboard once the chart has focus', () => {
    const { container } = draw();
    const chart = container.querySelector('svg')!;

    fireEvent.focus(chart);
    expect(within(screen.getByRole('tooltip')).getByText('2025-12')).toBeInTheDocument();

    fireEvent.keyDown(chart, { key: 'ArrowLeft' });
    expect(within(screen.getByRole('tooltip')).getByText('2025-11')).toBeInTheDocument();

    fireEvent.keyDown(chart, { key: 'Home' });
    expect(within(screen.getByRole('tooltip')).getByText('2015-01')).toBeInTheDocument();

    fireEvent.blur(chart);
    expect(screen.queryByRole('tooltip')).toBeNull();
  });

  it('leaves out a series the platform answered nothing for, legend and all', () => {
    const { container } = draw({
      ...widget,
      series: [widget.series[0], { label: 'Never answered', value: { kind: 'const', value: 0 } }],
    });

    expect(container.querySelectorAll('path[data-series]')).toHaveLength(1);
    expect(screen.queryByText('Never answered')).toBeNull();
  });

  it('names the chart and its series for a reader who cannot see it', () => {
    draw();

    expect(screen.getByRole('img', { name: /Average monthly temperature/ })).toBeInTheDocument();
  });
});
