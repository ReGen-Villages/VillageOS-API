import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, act } from '@testing-library/react';
import type { TableColumn } from '../../../types/dashboard';
import type { ResolveContext, Row } from '../../../api/dashboardApi';

vi.mock('../../../hooks/useDashboard', () => ({
  useBinding: () => ({ loading: false, error: false, value: null }),
}));

const observed: HTMLElement[] = [];
let reportHeight: ((height: number) => void) | null = null;

class RecordingResizeObserver {
  private callback: ResizeObserverCallback;
  constructor(callback: ResizeObserverCallback) {
    this.callback = callback;
  }
  observe(element: HTMLElement) {
    observed.push(element);
    reportHeight = (height) => {
      element.getBoundingClientRect = () => ({ height }) as DOMRect;
      this.callback([{ target: element } as unknown as ResizeObserverEntry], this as unknown as ResizeObserver);
    };
  }
  unobserve() {}
  disconnect() {
    reportHeight = null;
  }
}

const { DataTable } = await import('./DataTable');

const columns: TableColumn[] = [
  { key: 'name', label: 'Location' },
  { key: 'units', label: 'Units', numeric: true },
];

function renderTable({ visibleRows, rowCount = 8 }: { visibleRows?: number; rowCount?: number }) {
  const rows: Row[] = Array.from({ length: rowCount }, (_, i) => ({
    id: `location-${i}`,
    name: `Location ${i}`,
    units: i,
  }));
  const { container } = render(
    <DataTable columns={columns} rows={rows} ctx={{} as ResolveContext} visibleRows={visibleRows} />,
  );
  return container.querySelector('.overflow-x-auto') as HTMLElement;
}

describe('DataTable visibleRows cap (Test Case 6123)', () => {
  const originalObserver = globalThis.ResizeObserver;

  beforeEach(() => {
    observed.length = 0;
    globalThis.ResizeObserver = RecordingResizeObserver as unknown as typeof ResizeObserver;
  });

  afterEach(() => {
    globalThis.ResizeObserver = originalObserver;
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

    expect(observed).toHaveLength(1);
    expect(observed[0].tagName).toBe('TR');

    act(() => reportHeight!(48));

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
    expect(observed).toHaveLength(0);
  });
});
