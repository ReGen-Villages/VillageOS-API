import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';

vi.mock('../hooks/useSse', () => ({ useSubscription: () => {} }));

import { TemporalPage } from './TemporalPage';

const TAB_LABELS = [
  'Mutations', 'Thing Mutations', 'Relationship Mutations', 'Snapshot', 'Property History', 'State Query',
];

describe('the temporal page on a narrow screen', () => {
  it('scrolls its tabs sideways rather than cutting the last ones off', () => {
    render(<TemporalPage />);

    expect(screen.getByRole('button', { name: 'Mutations' }).parentElement).toHaveClass('overflow-x-auto');
  });

  it('wraps each query row, so no field is pushed past the edge', () => {
    const { container } = render(<TemporalPage />);

    for (const label of TAB_LABELS) {
      fireEvent.click(screen.getByRole('button', { name: label }));
      const rows = container.querySelectorAll('.items-end');
      expect(rows.length, label).toBeGreaterThan(0);
      for (const row of rows) expect(row, label).toHaveClass('flex-wrap');
    }
  });
});
