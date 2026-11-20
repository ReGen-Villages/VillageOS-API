import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { Binding, DensityGridWidget } from '@tendril-ui/tendril';
import type { BindingResult, ResolveContext } from '@tendril-ui/tendril';
import { rampColour } from './chartRamp';

import { values } from '../../../testBindingValues';

vi.mock('@tendril-ui/tendril', async (importOriginal) =>
  (await import('../../../testBindingValues')).withAnsweredBindings(importOriginal));

const { DensityGrid } = await import('./DensityGrid');

const TEN_YEARS = 10 * 365 * 86_400;

function paired(fold: 'cell' | 'monthOfYear,cell', rows: { key: string; value: number }[]): Extract<Binding, { kind: 'history' }> {
  const binding: Extract<Binding, { kind: 'history' }> = {
    kind: 'history', property: 'temperature', windowSeconds: TEN_YEARS,
    steps: [{ fold, function: 'Count' }],
    bins: { width: 2 }, pairedWith: { property: 'humidity', bins: { width: 5, upTo: 100 } },
  };
  values.set(JSON.stringify(binding), rows);
  return binding;
}

const cells = paired('cell', [
  { key: '20,40', value: 10 }, { key: '22,40', value: 20 }, { key: '22,95', value: 10 },
]);

function coverageOf(unpairedSamples: number): Binding {
  const binding: Binding = { ...cells, answer: 'coverage' };
  values.set(JSON.stringify(binding), [{ samples: 40, unpairedSamples, unusableSamples: 0 }]);
  return binding;
}

function shapeOf(property: string, value: unknown): Binding {
  const binding: Binding = { kind: 'related', via: [{ predicate: 'studies', direction: 'in' }], property };
  values.set(JSON.stringify(binding), value as BindingResult);
  return binding;
}

const widget: DensityGridWidget = {
  type: 'densityGrid',
  title: 'Humidity and temperature',
  cells,
  across: { label: 'Temperature', unit: '°C' },
  up: { label: 'Relative humidity', unit: '%' },
};

function draw(drawn: DensityGridWidget = widget) {
  return render(<DensityGrid widget={drawn} context={{} as ResolveContext} />);
}

const cellAt = (container: HTMLElement, across: string, up: string) =>
  container.querySelector(`rect[data-across="${across}"][data-up="${up}"]`);

describe('the density grid', () => {
  it('draws a cell at each pair of lower bounds, as wide and as tall as each axis\'s bins', () => {
    const { container } = draw();

    const drawn = [...container.querySelectorAll('rect[data-across]')];
    expect(drawn.map((cell) => `${cell.getAttribute('data-across')},${cell.getAttribute('data-up')}`)).toEqual(['20,40', '22,40', '22,95']);
    const first = cellAt(container, '20', '40')!;
    const beside = cellAt(container, '22', '40')!;
    expect(Number(beside.getAttribute('x')) - Number(first.getAttribute('x'))).toBeCloseTo(Number(first.getAttribute('width')), 5);
  });

  it('shades each cell by its share of the hours counted, the most common darkest', () => {
    const { container } = draw();

    expect(cellAt(container, '22', '40')!.getAttribute('data-share')).toBe('0.5');
    expect(cellAt(container, '20', '40')!.getAttribute('data-share')).toBe('0.25');
    expect(cellAt(container, '22', '40')!.getAttribute('fill')).toBe(rampColour(1, 'light'));
  });

  it('shades from the dark surface\'s ramp on a dark page', () => {
    document.documentElement.classList.add('dark');
    try {
      const { container } = draw();
      expect(cellAt(container, '22', '40')!.getAttribute('fill')).toBe(rampColour(1, 'dark'));
    } finally {
      document.documentElement.classList.remove('dark');
    }
  });

  it('says how many readings had no partner at their hour, and only when some had none', () => {
    draw({ ...widget, coverage: coverageOf(8) });
    expect(screen.getByText(/40 hours had both readings; 8 readings had no reading of the other at the same time/)).toBeTruthy();
  });

  it('writes no coverage note when every reading had a partner', () => {
    draw({ ...widget, coverage: coverageOf(0) });
    expect(screen.queryByText(/no reading of the other/)).toBeNull();
  });

  it('draws a polygon outline filled faintly and a line outline unfilled, in the chart\'s own units', () => {
    const zone = shapeOf('summerComfortZone', { type: 'Polygon', coordinates: [[[20, 40], [24, 40], [24, 60], [20, 60], [20, 40]]] });
    const curve = shapeOf('fiftyPercentCurve', JSON.stringify({ type: 'LineString', coordinates: [[20, 50], [24, 50]] }));
    const { container } = draw({
      ...widget,
      outlines: [{ label: 'Summer comfort', colour: '#e8a33d', shape: zone }, { label: '50 %', colour: '#888888', shape: curve }],
    });

    const polygon = container.querySelector('path[data-outline="Summer comfort"]')!;
    const line = container.querySelector('path[data-outline="50 %"]')!;
    expect(polygon.getAttribute('d')).toMatch(/Z$/);
    expect(polygon.getAttribute('fill')).toBe('#e8a33d');
    expect(line.getAttribute('d')).not.toMatch(/Z$/);
    expect(line.getAttribute('fill')).toBe('none');
    expect(screen.getByText('Summer comfort')).toBeTruthy();
  });

  it('places an outline on the same scale as the cells', () => {
    const zone = shapeOf('cellSizedZone', { type: 'Polygon', coordinates: [[[20, 40], [22, 40], [22, 45], [20, 45], [20, 40]]] });
    const { container } = draw({ ...widget, outlines: [{ label: 'One cell', colour: '#000000', shape: zone }] });

    const cell = cellAt(container, '20', '40')!;
    const corner = container.querySelector('path[data-outline="One cell"]')!.getAttribute('d')!.match(/^M([\d.]+),([\d.]+)/)!;
    expect(Number(corner[1])).toBeCloseTo(Number(cell.getAttribute('x')), 1);
    expect(Number(corner[2])).toBeCloseTo(Number(cell.getAttribute('y')) + Number(cell.getAttribute('height')), 1);
  });

  it('draws nothing for an outline whose property holds no shape it can read', () => {
    const broken = shapeOf('notAShape', 'twelve');
    const { container } = draw({ ...widget, outlines: [{ label: 'Broken', colour: '#000000', shape: broken }] });

    expect(container.querySelector('path[data-outline]')).toBeNull();
  });

  it('draws no grid for a fold by month and says it draws one grid', () => {
    const byMonth = paired('monthOfYear,cell', [{ key: '7,22,40', value: 5 }]);
    const { container } = draw({ ...widget, cells: byMonth });

    expect(container.querySelector('rect[data-across]')).toBeNull();
    expect(screen.getByText(/draws one grid for the whole window/)).toBeTruthy();
  });

  it('names a cell\'s two ranges and its share when the pointer is over it', () => {
    const { container } = draw();

    fireEvent.mouseEnter(cellAt(container, '22', '40')!);

    expect(screen.getByRole('tooltip').textContent).toContain('22–24 °C');
    expect(screen.getByRole('tooltip').textContent).toContain('40–45 %');
    expect(screen.getByRole('tooltip').textContent).toContain('50%');
  });

  it('lists every cell with its share in the table', () => {
    draw();

    fireEvent.click(screen.getByRole('button', { name: 'Table' }));

    expect(screen.getByRole('rowheader', { name: '22–24 °C, 95–100 %' })).toBeTruthy();
  });
});
