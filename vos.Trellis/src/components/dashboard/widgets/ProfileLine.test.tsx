import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { Binding, ProfileLineWidget } from '@tendril-ui/tendril';
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

const { ProfileLine } = await import('./ProfileLine');

function held(binding: Binding, value: unknown): Binding {
  values.set(JSON.stringify(binding), value as BindingResult);
  return binding;
}

const along = (rows: number, columns: number, values: (number | null)[]) => ({
  originLatitude: 25, originLongitude: 55, cellSizeMetres: 30, latitudeStepDegrees: 0.0003, longitudeStepDegrees: 0.0003,
  rows, columns, values,
});

const westToEast = held({ kind: 'value', property: 'westToEast' }, along(1, 5, [84, 112, null, 96, 70]));
const spanFrom = held({ kind: 'property', thing: '$scope', property: 'spanFrom' }, 30);
const spanTo = held({ kind: 'property', thing: '$scope', property: 'spanTo' }, 90);

const widget: ProfileLineWidget = {
  type: 'profileLine', title: 'West to east', profile: westToEast, span: { from: spanFrom, to: spanTo }, unit: 'm', format: 'integer',
};

function draw(drawn: ProfileLineWidget = widget) {
  return render(<ProfileLine widget={drawn} context={{} as ResolveContext} />);
}

describe('the profile line', () => {
  it('draws the known values as a line, broken where a value is not known', () => {
    const { container } = draw();

    const pieces = [...container.querySelectorAll('path[data-profile]')];
    expect(pieces).toHaveLength(2);
    expect(pieces[0].getAttribute('d')).toMatch(/^M[\d.]+,[\d.]+ L[\d.]+,[\d.]+$/);
  });

  it('shades the span from its start to its end along the line', () => {
    const { container } = draw();

    const shade = container.querySelector('rect[data-span]')!;
    const start = container.querySelector('circle[data-distance="30"]')!;
    const end = container.querySelector('circle[data-distance="90"]')!;
    expect(Number(shade.getAttribute('x'))).toBeCloseTo(Number(start.getAttribute('cx')), 5);
    expect(Number(shade.getAttribute('x')) + Number(shade.getAttribute('width'))).toBeCloseTo(Number(end.getAttribute('cx')), 5);
  });

  it('writes the span\'s lowest, highest and difference under the chart', () => {
    draw();

    expect(screen.getByText('Lowest 96 m, highest 112 m, difference 16 m')).toBeTruthy();
  });

  it('writes the whole line\'s figures and shades nothing where there is no span', () => {
    const { container } = draw({ ...widget, span: undefined });

    expect(container.querySelector('rect[data-span]')).toBeNull();
    expect(screen.getByText('Lowest 70 m, highest 112 m, difference 42 m')).toBeTruthy();
  });

  it('runs south to north along a one-column grid', () => {
    const southToNorth = held({ kind: 'value', property: 'southToNorth' }, along(3, 1, [10, 20, 30]));
    const { container } = draw({ ...widget, profile: southToNorth, span: undefined });

    expect([...container.querySelectorAll('circle[data-distance]')].map((point) => point.getAttribute('data-distance'))).toEqual(['0', '30', '60']);
  });

  it('draws nothing and says so for a grid of more than one row and column', () => {
    const square = held({ kind: 'value', property: 'square' }, along(2, 2, [1, 2, 3, 4]));
    const { container } = draw({ ...widget, profile: square });

    expect(container.querySelector('path[data-profile]')).toBeNull();
    expect(screen.getByText('A profile reads a grid of one row or one column.')).toBeTruthy();
  });

  it('names a point\'s distance along the line and its value when the pointer is over it', () => {
    const { container } = draw();

    fireEvent.mouseEnter(container.querySelector('circle[data-distance="30"]')!);

    expect(screen.getByRole('tooltip').textContent).toContain('30 m along the line');
    expect(screen.getByRole('tooltip').textContent).toContain('112 m');
  });

  it('shows no tooltip once the pointer leaves the point', () => {
    const { container } = draw();
    fireEvent.mouseEnter(container.querySelector('circle[data-distance="30"]')!);

    fireEvent.mouseLeave(container.querySelector('circle[data-distance="30"]')!);

    expect(screen.queryByRole('tooltip')).toBeNull();
  });
});
