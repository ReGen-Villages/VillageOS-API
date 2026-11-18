import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { Binding, GridMapWidget } from '@tendril-ui/tendril';
import type { BindingResult, ResolveContext } from '@tendril-ui/tendril';
import { rampColour } from './chartRamp';

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

const { GridMap } = await import('./GridMap');

function held(property: string, value: unknown): Binding {
  const binding: Binding = { kind: 'value', property };
  values.set(JSON.stringify(binding), value as BindingResult);
  return binding;
}

const heights = held('heights', {
  originLatitude: 25.1, originLongitude: 55.3, cellSizeMetres: 30, latitudeStepDegrees: 0.001, longitudeStepDegrees: 0.001,
  rows: 2, columns: 3, values: [84, 90, null, 96, 102, 112],
});

const boundary = held('boundary', {
  type: 'Polygon', coordinates: [[[55.3, 25.1], [55.302, 25.1], [55.302, 25.101], [55.3, 25.101], [55.3, 25.1]]],
});

const widget: GridMapWidget = { type: 'gridMap', title: 'Elevation', grid: heights, outline: boundary, unit: 'm' };

function draw(drawn: GridMapWidget = widget) {
  return render(<GridMap widget={drawn} context={{} as ResolveContext} />);
}

const cellAt = (container: HTMLElement, row: number, column: number) =>
  container.querySelector(`rect[data-row="${row}"][data-column="${column}"]`);

describe('the grid map', () => {
  it('draws a cell for each known value and leaves an unknown one blank', () => {
    const { container } = draw();

    expect(container.querySelectorAll('rect[data-row]')).toHaveLength(5);
    expect(cellAt(container, 0, 2)).toBeNull();
  });

  it('places rows northward and columns eastward', () => {
    const { container } = draw();

    const southWest = cellAt(container, 0, 0)!;
    const northEast = cellAt(container, 1, 2)!;
    expect(Number(northEast.getAttribute('x'))).toBeGreaterThan(Number(southWest.getAttribute('x')));
    expect(Number(northEast.getAttribute('y'))).toBeLessThan(Number(southWest.getAttribute('y')));
  });

  it('shades from the lowest value to the highest on one hue', () => {
    const { container } = draw();

    expect(cellAt(container, 0, 0)!.getAttribute('fill')).toBe(rampColour(0, 'light'));
    expect(cellAt(container, 1, 2)!.getAttribute('fill')).toBe(rampColour(1, 'light'));
  });

  it('shades on a logarithmic scale when the widget asks for one', () => {
    const area = held('drainageArea', {
      originLatitude: 25.1, originLongitude: 55.3, cellSizeMetres: 30, latitudeStepDegrees: 0.001, longitudeStepDegrees: 0.001,
      rows: 1, columns: 3, values: [1, 100, 10_000],
    });
    const { container } = draw({ ...widget, grid: area, outline: undefined, scale: 'logarithmic' });

    expect(cellAt(container, 0, 1)!.getAttribute('fill')).toBe(rampColour(0.5, 'light'));
  });

  it('colours each cell by the first class holding its value, and names the classes in the legend', () => {
    values.set(JSON.stringify({ kind: 'const', value: 95 }), 95);
    const { container } = draw({
      ...widget,
      classes: [
        { label: 'Low', colour: '#a6d96a', to: { kind: 'const', value: 95 } },
        { label: 'High', colour: '#d7191c', from: { kind: 'const', value: 95 } },
      ],
    });

    expect(cellAt(container, 0, 0)!.getAttribute('fill')).toBe('#a6d96a');
    expect(cellAt(container, 1, 2)!.getAttribute('fill')).toBe('#d7191c');
    expect(screen.getByText('Low')).toBeTruthy();
  });

  it('draws the site\'s outline dashed over the cells', () => {
    const { container } = draw();

    const outline = container.querySelector('path[data-outline]')!;
    expect(outline.getAttribute('stroke-dasharray')).toBeTruthy();
    expect(outline.getAttribute('fill')).toBe('none');
    expect(outline.getAttribute('d')).toMatch(/^M[\d.]+,[\d.]+( L[\d.]+,[\d.]+)+ Z$/);
  });

  it('draws no outline read across a relationship, though the binding answers one', () => {
    const relatedShape: Binding = { kind: 'value', via: [{ predicate: 'outlines', direction: 'in' }], property: 'boundary' };
    values.set(JSON.stringify(relatedShape), values.get(JSON.stringify(boundary)) ?? null);

    const { container } = draw({ ...widget, outline: relatedShape });

    expect(container.querySelector('path[data-outline]')).toBeNull();
    expect(container.querySelectorAll('rect[data-row]')).toHaveLength(5);
  });

  it('draws nothing for a value that is not a grid', () => {
    const { container } = draw({ ...widget, grid: held('notAGrid', 'twelve') });

    expect(container.querySelector('rect[data-row]')).toBeNull();
  });

  function gridOfCells(property: string, rows: number, columns: number): Binding {
    return held(property, {
      originLatitude: 25.1, originLongitude: 55.3, cellSizeMetres: 30, latitudeStepDegrees: 0.0003, longitudeStepDegrees: 0.0003,
      rows, columns, values: Array.from({ length: rows * columns }, (_, at) => at % 97),
    });
  }

  it('draws a grid of as many cells as it draws at most', () => {
    const { container } = draw({ ...widget, grid: gridOfCells('mostDrawn', 100, 100), outline: undefined });

    expect(container.querySelectorAll('rect[data-row]')).toHaveLength(10_000);
    expect(screen.queryByText(/draws at most/)).toBeNull();
  });

  it('draws no cells of a grid one cell larger, and says how many it holds and the most it draws', () => {
    const { container } = draw({ ...widget, grid: gridOfCells('oneTooMany', 1, 10_001) });

    expect(container.querySelector('rect[data-row]')).toBeNull();
    expect(screen.getByText('This grid holds 10,001 cells; a grid map draws at most 10,000.')).toBeTruthy();
    expect(screen.queryByRole('button', { name: 'Table' })).toBeNull();
  });

  it('draws no cells of a grid as large as the platform holds, without overflowing', () => {
    const { container } = draw({ ...widget, grid: gridOfCells('platformLimit', 256, 256), outline: undefined });

    expect(container.querySelector('rect[data-row]')).toBeNull();
    expect(screen.getByText('This grid holds 65,536 cells; a grid map draws at most 10,000.')).toBeTruthy();
  });

  it('names where a cell is, as the table numbers it, and its value once, when the pointer is over it', () => {
    const { container } = draw();

    fireEvent.mouseEnter(cellAt(container, 1, 2)!);

    const tooltip = screen.getByRole('tooltip');
    expect(tooltip.textContent).toContain('Row 2, column 3');
    expect(tooltip.textContent!.split('112 m')).toHaveLength(2);
  });
});
