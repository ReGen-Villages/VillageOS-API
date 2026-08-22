import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, act, fireEvent } from '@testing-library/react';
import type { TableColumn } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';
import { installResizeObserverDouble } from '../../../testResizeObserver';

vi.mock('../../../hooks/useDashboard', () => ({
  useBinding: () => ({ loading: false, error: false, value: null }),
}));

const { DataTable } = await import('./DataTable');

const columns: TableColumn[] = [
  { key: 'name', label: 'Location' },
  { key: 'units', label: 'Units', numeric: true },
];

/** The height the ResizeObserver double reports for the header row and for a body row alike,
 *  so a window's spacer heights are a whole multiple of it. */
const REPORTED_ROW_HEIGHT = 40;

function renderTable({
  visibleRows,
  rowCount = 8,
  sortKey,
  sortDir,
  query,
}: {
  visibleRows?: number;
  rowCount?: number;
  sortKey?: string;
  sortDir?: 'asc' | 'desc';
  query?: string;
}) {
  const rows: Row[] = Array.from({ length: rowCount }, (_, i) => ({
    id: `location-${i}`,
    name: `Location ${i}`,
    units: i,
  }));
  const { container } = render(
    <DataTable
      columns={columns}
      rows={rows}
      ctx={{} as ResolveContext}
      visibleRows={visibleRows}
      sortKey={sortKey}
      sortDir={sortDir}
      query={query}
    />,
  );
  return container.querySelector('.overflow-x-auto') as HTMLElement;
}

/** A long list in its natural order: sorting by the numeric column ascending makes the row at
 *  index N `Location N`, which is what the window assertions read. */
function renderLongTable(extra: { query?: string } = {}) {
  return renderTable({ visibleRows: 10, rowCount: 500, sortKey: 'units', sortDir: 'asc', ...extra });
}

function dataRows(scroller: HTMLElement): HTMLElement[] {
  return Array.from(scroller.querySelectorAll('tbody tr:not([aria-hidden="true"])'));
}

function rowNames(scroller: HTMLElement): (string | null)[] {
  return dataRows(scroller).map((row) => row.querySelector('td')?.textContent ?? null);
}

function spacerHeights(scroller: HTMLElement): number[] {
  return Array.from(scroller.querySelectorAll<HTMLElement>('tbody tr[aria-hidden="true"] td')).map((cell) =>
    parseFloat(cell.style.height),
  );
}

/** jsdom performs no layout, so `scrollTop` never takes a written value. Define it, then fire the
 *  event the table listens for. */
function scrollTo(scroller: HTMLElement, scrollTop: number) {
  Object.defineProperty(scroller, 'scrollTop', { value: scrollTop, configurable: true });
  fireEvent.scroll(scroller);
}

describe('DataTable visibleRows cap (Test Case 6123)', () => {
  let observer: ReturnType<typeof installResizeObserverDouble>;

  beforeEach(() => {
    observer = installResizeObserverDouble();
  });

  afterEach(() => {
    observer.restore();
  });

  it('caps a table at the requested row count with a vertical scroll container', () => {
    const scroller = renderTable({ visibleRows: 3 });

    expect(scroller.className).toContain('overflow-y-auto');
    expect(scroller.style.maxHeight).toContain('3 *');
    expect(scroller.style.maxHeight).toContain('em');
    expect(scroller.querySelectorAll('tbody tr')).toHaveLength(8);
  });

  it('adds the measured header height so the capped rows are not clipped by it', () => {
    const scroller = renderTable({ visibleRows: 3 });

    expect(observer.observed).toHaveLength(1);
    expect(observer.observed[0].tagName).toBe('TR');

    act(() => observer.report({ height: 48 }));

    expect(scroller.style.maxHeight).toContain('48px');
  });

  it('sizes the cap to the row count', () => {
    const capOfThree = renderTable({ visibleRows: 3 }).style.maxHeight;
    const capOfFive = renderTable({ visibleRows: 5 }).style.maxHeight;

    expect(capOfFive).not.toBe(capOfThree);
  });

  it('reserves no space below a table shorter than the cap', () => {
    const scroller = renderTable({ visibleRows: 3, rowCount: 2 });

    expect(scroller.style.maxHeight).not.toBe('');
    expect(scroller.style.height).toBe('');
  });

  it('renders an uncapped table without a scroll container or a header measurement', () => {
    const scroller = renderTable({});

    expect(scroller.style.maxHeight).toBe('');
    expect(scroller.className).not.toContain('overflow-y-auto');
    expect(observer.observed).toHaveLength(0);
  });
});

describe('DataTable row window (Bug 6583)', () => {
  let observer: ReturnType<typeof installResizeObserverDouble>;

  beforeEach(() => {
    observer = installResizeObserverDouble();
  });

  afterEach(() => {
    observer.restore();
  });

  it('puts a bounded number of rows in the document when the list runs past the cap', () => {
    const scroller = renderLongTable();
    act(() => observer.report({ height: REPORTED_ROW_HEIGHT }));

    const shown = rowNames(scroller);
    expect(shown.length).toBeLessThan(40);
    expect(shown[0]).toBe('Location 0');
    expect(shown).not.toContain('Location 499');
  });

  it('measures a rendered body row, not only the header', () => {
    renderLongTable();

    expect(observer.observed.map((element) => element.tagName)).toEqual(['TR', 'TR']);
  });

  it('moves the window as the table is scrolled', () => {
    const scroller = renderLongTable();
    act(() => observer.report({ height: REPORTED_ROW_HEIGHT }));
    expect(rowNames(scroller)).toContain('Location 0');

    scrollTo(scroller, 300 * REPORTED_ROW_HEIGHT);

    const shown = rowNames(scroller);
    expect(shown).toContain('Location 300');
    expect(shown).not.toContain('Location 0');
  });

  it('holds the height of every row the list has, so the scrollbar reflects all of them', () => {
    const scroller = renderLongTable();
    act(() => observer.report({ height: REPORTED_ROW_HEIGHT }));
    scrollTo(scroller, 300 * REPORTED_ROW_HEIGHT);

    const held = spacerHeights(scroller).reduce((total, height) => total + height, 0);

    expect(held + dataRows(scroller).length * REPORTED_ROW_HEIGHT).toBe(500 * REPORTED_ROW_HEIGHT);
  });

  it('keeps the last rows reachable at the bottom of the list', () => {
    const scroller = renderLongTable();
    act(() => observer.report({ height: REPORTED_ROW_HEIGHT }));

    scrollTo(scroller, 500 * REPORTED_ROW_HEIGHT);

    expect(rowNames(scroller)).toContain('Location 499');
  });

  it('searches every row the list holds, not only the rows in the window', () => {
    const scroller = renderLongTable({ query: 'Location 480' });
    act(() => observer.report({ height: REPORTED_ROW_HEIGHT }));

    expect(rowNames(scroller)).toEqual(['Location 480']);
  });

  it('follows the container back to the top when a search narrows the list inside the cap', () => {
    const rows: Row[] = Array.from({ length: 500 }, (_, i) => ({ id: `location-${i}`, name: `Location ${i}`, units: i }));
    const table = (query?: string) => (
      <DataTable columns={columns} rows={rows} ctx={{} as ResolveContext} visibleRows={10} sortKey="units" sortDir="asc" query={query} />
    );
    const { container, rerender } = render(table());
    const scroller = container.querySelector('.overflow-x-auto') as HTMLElement;
    act(() => observer.report({ height: REPORTED_ROW_HEIGHT }));
    scrollTo(scroller, 300 * REPORTED_ROW_HEIGHT);

    rerender(table('Location 480'));
    scrollTo(scroller, 0);
    rerender(table());

    expect(rowNames(container.querySelector('.overflow-x-auto') as HTMLElement)).toContain('Location 0');
  });

  it('stops re-measuring once a row height comes back, rather than once per row crossed', () => {
    const scroller = renderLongTable();
    act(() => observer.report({ height: REPORTED_ROW_HEIGHT }));
    const afterMeasuring = observer.observed.length;

    scrollTo(scroller, 100 * REPORTED_ROW_HEIGHT);
    scrollTo(scroller, 200 * REPORTED_ROW_HEIGHT);
    scrollTo(scroller, 300 * REPORTED_ROW_HEIGHT);

    expect(observer.observed).toHaveLength(afterMeasuring);
  });

  it('renders every row when the list fits inside the cap', () => {
    const scroller = renderTable({ visibleRows: 10, rowCount: 12, sortKey: 'units', sortDir: 'asc' });

    expect(rowNames(scroller)).toHaveLength(12);
    expect(spacerHeights(scroller)).toEqual([]);
  });
});
