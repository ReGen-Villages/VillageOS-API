import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { Binding } from '../../../types/dashboard';
import { buildModelIndex, type ResolveContext } from '../../../api/dashboardApi';
import type { FigureBreakdown } from '../../../api/figureBreakdown';

let answer: { loading: boolean; breakdown: FigureBreakdown | null } = { loading: false, breakdown: null };

vi.mock('../../../hooks/useDashboard', () => ({
  useBinding: () => ({ loading: false, error: false, value: null }),
}));
vi.mock('../../../hooks/useFigureBreakdown', () => ({
  useFigureBreakdown: () => answer,
}));

const { FigurePanel } = await import('./FigurePanel');

const BINDING: Binding = { kind: 'stateCount', state: 'flooded', archetype: 'Catchment' };
const CONTEXT = { idx: buildModelIndex([], []), scopeId: null } as ResolveContext;

function show(breakdown: FigureBreakdown | null, props: Partial<Parameters<typeof FigurePanel>[0]> = {}) {
  answer = { loading: false, breakdown };
  return render(<FigurePanel binding={BINDING} ctx={CONTEXT} title="Flooded catchments" onClose={() => {}} {...props} />);
}

const COUNTED: FigureBreakdown = {
  value: 2,
  terms: { archetype: 'Catchment', state: 'flooded', within: 'North ridge' },
  behind: {
    kind: 'things',
    reduction: 'count',
    measure: null,
    rows: [
      { id: 'catchment-1', name: 'CATCH-1', area: 100 },
      { id: 'catchment-2', name: 'CATCH-2', area: 300 },
    ],
  },
};

describe('a figure opened up', () => {
  it('shows the figure and one row per Thing behind it', () => {
    show(COUNTED);

    expect(screen.getByText('2')).toBeInTheDocument();
    expect(screen.getByText('CATCH-1')).toBeInTheDocument();
    expect(screen.getByText('CATCH-2')).toBeInTheDocument();
    expect(screen.getByText('2 things behind this figure', { exact: false })).toBeInTheDocument();
  });

  it("names the model's own words for what the figure counts", () => {
    show(COUNTED);

    expect(screen.getByText('Catchment')).toBeInTheDocument();
    expect(screen.getByText('flooded')).toBeInTheDocument();
    expect(screen.getByText('North ridge')).toBeInTheDocument();
  });

  it('opens the clicked Thing’s own card', () => {
    const openDetail = vi.fn();
    show(COUNTED, { openDetail });

    fireEvent.click(screen.getByText('CATCH-2'));

    expect(openDetail).toHaveBeenCalledWith('catchment-2');
  });

  it('closes on Escape', () => {
    const onClose = vi.fn();
    show(COUNTED, { onClose });

    fireEvent.keyDown(document, { key: 'Escape' });

    expect(onClose).toHaveBeenCalled();
  });

  it('narrows the rows to what the reader searched for, and clearing brings them all back', () => {
    show(COUNTED);

    fireEvent.change(screen.getByPlaceholderText('Search…'), { target: { value: 'CATCH-1' } });
    expect(screen.getByText('CATCH-1')).toBeInTheDocument();
    expect(screen.queryByText('CATCH-2')).toBeNull();

    fireEvent.click(screen.getByPlaceholderText('Search…').parentElement!.querySelector('button')!);
    expect(screen.getByText('CATCH-2')).toBeInTheDocument();
  });

  it('says which of a row’s properties the table had no column for', () => {
    const row: Record<string, unknown> = { id: 'a', name: 'A' };
    for (const key of ['a', 'b', 'c', 'd', 'e', 'f', 'g']) row[key] = 1;
    show({ ...COUNTED, value: 1, behind: { kind: 'things', reduction: 'count', measure: null, rows: [row] } });

    expect(screen.getByText(/Each row holds more than this table shows: g/)).toBeInTheDocument();
  });

  it('names the measure each row contributed to a reduction', () => {
    show({
      value: 240,
      terms: { archetype: 'Catchment', property: 'stored' },
      behind: { kind: 'things', reduction: 'sum', measure: 'stored', rows: [{ id: '1', name: 'CATCH-1', stored: 240 }] },
    });

    expect(screen.getByText(/Each row contributed its stored\. The figure is their total\./)).toBeInTheDocument();
  });
});

describe('a division opened up', () => {
  const DIVIDED: FigureBreakdown = {
    value: 0.6,
    terms: {},
    behind: {
      kind: 'division',
      numerator: {
        value: 240,
        terms: { property: 'stored' },
        behind: {
          kind: 'things',
          reduction: 'sum',
          measure: 'stored',
          rows: [
            { id: '1', name: 'CATCH-1', stored: 140 },
            { id: '2', name: 'CATCH-2', stored: 100 },
          ],
        },
      },
      denominator: { value: 400, terms: {}, behind: null },
    },
  };

  it('shows both sides, each with its own figure', () => {
    show(DIVIDED, { format: 'percent' });

    const top = screen.getByText('What is divided').closest('section')!;
    const bottom = screen.getByText('What it is divided by').closest('section')!;
    expect(within(top).getByText('240')).toBeInTheDocument();
    expect(within(bottom).getByText('400')).toBeInTheDocument();
  });

  it('says plainly when a side has nothing to list', () => {
    show(DIVIDED);

    const bottom = screen.getByText('What it is divided by').closest('section')!;
    expect(within(bottom).getByText('Nothing behind this figure can be listed.')).toBeInTheDocument();
  });
});

describe('a trailing-window figure opened up', () => {
  it('draws one cell per part of the window it was reduced over', () => {
    show({
      value: 500,
      terms: { archetype: 'Reading', happenedAt: 'taken_at', property: 'litres' },
      behind: { kind: 'buckets', reduction: 'sum', values: [100, 200, 200], bucketSeconds: 1200, windowSeconds: 3600, endsAt: Date.parse('2026-08-23T12:00:00Z') },
    });

    expect(screen.getByText(/in 3 equal parts, oldest first/)).toBeInTheDocument();
    expect(screen.getByText('100')).toBeInTheDocument();
    expect(screen.getAllByText('200')).toHaveLength(2);
  });
});

describe('a figure the console cannot take apart', () => {
  it('says so rather than showing an empty table', () => {
    show(null);

    expect(screen.getByText('Nothing behind this figure can be listed.')).toBeInTheDocument();
  });
});
