import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen, fireEvent, within, act } from '@testing-library/react';
import type { Binding, PolarSharesWidget } from '@tendril-ui/tendril';
import type { BindingResult, ResolveContext } from '@tendril-ui/tendril';
import i18n from '../../../i18n';

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

const { PolarShares } = await import('./PolarShares');

const TEN_YEARS = 10 * 365 * 86_400;
const BEARINGS = Array.from({ length: 16 }, (_, sector) => sector * 22.5);
const CLASSES = [
  { name: 'calm', order: 1, colour: '#cfe3f3' },
  { name: 'breeze', order: 2, colour: '#3b7dd8' },
];

/** One cell Thing per sector and speed class, as a thing list with its related columns answers them. */
const cellRows = BEARINGS.flatMap((bearing, sector) => CLASSES.map((speed) => ({
  id: `C${sector}-${speed.order}`,
  name: `${bearing} ${speed.name}`,
  centreDegrees: bearing,
  classOrder: speed.order,
  className: speed.name,
  classColour: speed.colour,
})));

const cells: Binding = { kind: 'thingList', archetype: 'WindRoseCell' };
values.set(JSON.stringify(cells), cellRows);

function counted(fold: 'value' | 'monthOfYear,value', rows: { key: string; value: number }[]): Binding {
  const binding: Binding = { kind: 'history', property: 'windRoseCell', windowSeconds: TEN_YEARS, steps: [{ fold, function: 'Count' }] };
  values.set(JSON.stringify(binding), rows);
  return binding;
}

/** North blows most, east half as much, every other sector a little; the platform keys cells lower-case. */
const year = counted('value', [
  { key: 'c0-1', value: 30 }, { key: 'c0-2', value: 10 },
  { key: 'c4-1', value: 15 }, { key: 'c4-2', value: 5 },
  ...BEARINGS.slice(1).filter((_, at) => at !== 3).map((_, at) => ({ key: `c${at < 3 ? at + 1 : at + 2}-1`, value: 4 })),
  { key: 'c99-1', value: 50 },
]);

const widget: PolarSharesWidget = {
  type: 'polarShares',
  title: 'Wind rose',
  counts: year,
  cells,
  bearingKey: 'centreDegrees',
  classOrderKey: 'classOrder',
  classLabelKey: 'className',
  classColourKey: 'classColour',
};

function draw(drawn: PolarSharesWidget = widget) {
  return render(<PolarShares widget={drawn} context={{} as ResolveContext} />);
}

const outerRadius = (wedge: Element) => Number(wedge.getAttribute('data-outer'));

describe('the polar share chart', () => {
  it('draws a wedge for each sector and class present, coloured by its class and stacked outward in class order', () => {
    const { container } = draw();

    const north = container.querySelector('[data-sector="0"]')!;
    const wedges = [...north.querySelectorAll('path[data-class]')];
    expect(wedges.map((wedge) => wedge.getAttribute('fill'))).toEqual(['#cfe3f3', '#3b7dd8']);
    expect(outerRadius(wedges[1])).toBeGreaterThan(outerRadius(wedges[0]));
    expect(container.querySelectorAll('[data-sector]')).toHaveLength(16);
  });

  it('reaches out in proportion to each sector\'s share of the hours counted, on one scale', () => {
    const { container } = draw();

    const reachOf = (sector: string) => {
      const wedges = [...container.querySelectorAll(`[data-sector="${sector}"] path[data-class]`)];
      return outerRadius(wedges[wedges.length - 1]);
    };
    expect(reachOf('90') / reachOf('0')).toBeCloseTo(20 / 40, 5);
  });

  it('leaves out a count whose key names no cell the model declares', () => {
    draw();

    fireEvent.focus(screen.getByRole('img', { name: /^0°/ }));

    // 30 + 10 of the 116 hours that name a declared cell; the 50 under an undeclared key are not counted.
    expect(within(screen.getByRole('tooltip')).getByText('26%')).toBeInTheDocument();
  });

  it('draws twelve roses on one scale from a count by month', () => {
    const byMonth = counted('monthOfYear,value', [
      { key: '1,c0-1', value: 10 }, { key: '1,c4-1', value: 10 },
      { key: '7,c0-1', value: 40 },
    ]);
    const { container } = draw({ ...widget, counts: byMonth });

    expect([...container.querySelectorAll('[data-month]')].map((rose) => rose.getAttribute('data-month'))).toEqual(['1', '7']);
    const reach = (month: string, sector: string) =>
      outerRadius(container.querySelector(`[data-month="${month}"] [data-sector="${sector}"] path[data-class]`)!);
    expect(reach('1', '0') / reach('7', '0')).toBeCloseTo(0.5, 5);
  });

  it('carries a legend of the classes in order and a table twin of every sector', () => {
    draw();

    expect(within(screen.getByRole('list')).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['calm', 'breeze']);
    const rows = within(screen.getByRole('table')).getAllByRole('row');
    expect(rows).toHaveLength(17);
    expect(rows[1].textContent).toBe('0°26%9%');
  });

  it('draws nothing where neither the counts nor the cells are answered', () => {
    const { container } = draw({ ...widget, counts: { kind: 'const', value: 0 } });

    expect(container.querySelectorAll('[data-sector]')).toHaveLength(0);
  });
});

describe('the polar share chart on a page in another language', () => {
  function wordedCells(archetype: string, wordingOf: (order: number) => string | undefined): Binding {
    const binding: Binding = { kind: 'thingList', archetype };
    values.set(JSON.stringify(binding), cellRows.map((row) => ({ ...row, classWording: wordingOf(row.classOrder) })));
    return binding;
  }

  async function drawIn(language: string, cellsBinding: Binding) {
    await act(() => i18n.changeLanguage(language));
    return draw({ ...widget, cells: cellsBinding, classWordingKey: 'classWording' });
  }

  afterEach(async () => {
    await act(() => i18n.changeLanguage('en'));
  });

  it('labels each class in the legend, the tooltip and the table with its words in the page language', async () => {
    await drawIn('de', wordedCells('GermanCells', (order) => JSON.stringify(
      order === 1 ? { en: 'Calm', de: 'Windstill' } : { en: 'Breeze', de: 'Brise' })));

    expect(within(screen.getByRole('list')).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Windstill', 'Brise']);
    expect(within(screen.getByRole('table')).getAllByRole('columnheader').map((header) => header.textContent)).toContain('Windstill');
    fireEvent.focus(screen.getByRole('img', { name: /^0°/ }));
    expect(within(screen.getByRole('tooltip')).getByText('Brise')).toBeInTheDocument();
  });

  it('falls back to the English words, then to the label column, where the page language has none', async () => {
    await drawIn('de', wordedCells('EnglishOnlyCells', (order) => (order === 1 ? JSON.stringify({ en: 'Calm' }) : undefined)));

    expect(within(screen.getByRole('list')).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['Calm', 'breeze']);
  });

  it('reads a wording column that is not words by language as no wording, and shows the label column', async () => {
    await drawIn('de', wordedCells('MalformedCells', (order) => (order === 1 ? '{"de": ' : JSON.stringify(['Brise']))));

    expect(within(screen.getByRole('list')).getAllByRole('listitem').map((item) => item.textContent)).toEqual(['calm', 'breeze']);
  });
});
