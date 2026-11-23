import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import type { Binding, RaisedSurfaceWidget } from '@tendril-ui/tendril';
import type { BindingResult, ResolveContext } from '@tendril-ui/tendril';
import type { RaisedSurface as Surface } from './surfaceGeometry';

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

// The scene needs a graphics card; this stand-in shows what it was handed.
vi.mock('./RaisedSurfaceScene', () => ({
  default: ({ surface, colours }: { surface: Surface; colours: string[] }) => (
    <div data-testid="scene" data-triangles={surface.indices.length / 3} data-box={surface.boxHeight} data-colours={new Set(colours).size} />
  ),
}));

const { RaisedSurface } = await import('./RaisedSurface');

function held(property: string, rows: number, columns: number, numbers: (number | null)[]): Binding {
  const binding: Binding = { kind: 'value', property };
  values.set(JSON.stringify(binding), {
    originLatitude: 25, originLongitude: 55, cellSizeMetres: 10, latitudeStepDegrees: 0.0001, longitudeStepDegrees: 0.0001,
    rows, columns, values: numbers,
  });
  return binding;
}

const widget: RaisedSurfaceWidget = { type: 'raisedSurface', title: 'Surface', surface: held('levels', 2, 2, [100, 100.5, 101, 101.2]), unit: 'm', exaggeration: 3 };

const browserGives = (context: unknown) =>
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as never);

beforeEach(() => { browserGives({ getExtension: () => null }); });

afterEach(() => vi.restoreAllMocks());

describe('the raised surface', () => {
  it('hands the scene the grid raised by the exaggeration, within its box', async () => {
    render(<RaisedSurface widget={widget} context={{} as ResolveContext} />);

    const scene = await screen.findByTestId('scene');
    expect(scene.getAttribute('data-triangles')).toBe('2');
    expect(Number(scene.getAttribute('data-box'))).toBeCloseTo(3.6, 5);
    expect(scene.getAttribute('data-colours')).toBe('4');
  });

  it('raises the grid by its own values where no exaggeration is given', async () => {
    render(<RaisedSurface widget={{ ...widget, exaggeration: undefined }} context={{} as ResolveContext} />);

    expect(Number((await screen.findByTestId('scene')).getAttribute('data-box'))).toBeCloseTo(1.2, 5);
  });

  it('writes the lowest and highest values alone where no unit is given', async () => {
    render(<RaisedSurface widget={{ ...widget, unit: undefined }} context={{} as ResolveContext} />);

    await screen.findByTestId('scene');
    expect(screen.getByRole('img', { name: 'Surface. From 100 to 101.2' })).toBeTruthy();
  });

  it('is named by its title and its lowest and highest values', async () => {
    render(<RaisedSurface widget={widget} context={{} as ResolveContext} />);

    await screen.findByTestId('scene');
    expect(screen.getByRole('img', { name: 'Surface. From 100 m to 101.2 m' })).toBeTruthy();
  });

  it('lists its values in the table from the northernmost row, a dash for an unknown one', async () => {
    render(<RaisedSurface widget={{ ...widget, surface: held('oneUnknown', 2, 2, [100, null, 101, 101.2]) }} context={{} as ResolveContext} />);

    await screen.findByTestId('scene');
    fireEvent.click(screen.getByRole('button', { name: 'Table' }));
    const rows = screen.getAllByRole('row').slice(1).map((row) => [...row.children].map((cell) => cell.textContent));
    expect(rows).toEqual([['2', '101 m', '101.2 m'], ['1', '100 m', '—']]);
  });

  it('names the lowest and highest values under the drawing', async () => {
    render(<RaisedSurface widget={widget} context={{} as ResolveContext} />);

    await screen.findByTestId('scene');
    expect(screen.getByText('100 m')).toBeTruthy();
    expect(screen.getByText('101.2 m')).toBeTruthy();
  });

  it('offers its table up to as many cells as a grid map draws, and says so for a larger grid', async () => {
    const atTheLimit = held('atTheLimit', 100, 100, new Array(10_000).fill(1));
    const { unmount } = render(<RaisedSurface widget={{ ...widget, surface: atTheLimit }} context={{} as ResolveContext} />);
    await screen.findByTestId('scene');
    expect(screen.getByRole('button', { name: 'Table' })).toBeTruthy();
    expect(screen.queryByText(/its table is offered/)).toBeNull();
    unmount();

    render(<RaisedSurface widget={{ ...widget, surface: held('oneOver', 73, 137, new Array(10_001).fill(1)) }} context={{} as ResolveContext} />);
    await screen.findByTestId('scene');
    expect(screen.queryByRole('button', { name: 'Table' })).toBeNull();
    expect(screen.getByText('This grid holds 10,001 cells; its table is offered up to 10,000.')).toBeTruthy();
  });

  it('draws nothing and says so for a grid of one row or one column', () => {
    render(<RaisedSurface widget={{ ...widget, surface: held('line', 1, 3, [1, 2, 3]) }} context={{} as ResolveContext} />);

    expect(screen.queryByTestId('scene')).toBeNull();
    expect(screen.getByText('A raised surface needs a grid of at least two rows and two columns.')).toBeTruthy();
  });

  it('says a grid of two rows and two columns with no known value holds none', () => {
    render(<RaisedSurface widget={{ ...widget, surface: held('unknown', 2, 2, [null, null, null, null]) }} context={{} as ResolveContext} />);

    expect(screen.queryByTestId('scene')).toBeNull();
    expect(screen.getByText('This grid holds no known value.')).toBeTruthy();
  });

  it('says so in place of the drawing where the browser draws no 3D, and leaves the figures and the table', async () => {
    browserGives(null);
    render(<RaisedSurface widget={widget} context={{} as ResolveContext} />);

    expect(await screen.findByText('The 3D drawing could not be shown here.')).toBeTruthy();
    expect(screen.queryByTestId('scene')).toBeNull();
    expect(screen.queryByRole('img')).toBeNull();
    expect(screen.getByText('100 m')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Table' })).toBeTruthy();
  });
});
