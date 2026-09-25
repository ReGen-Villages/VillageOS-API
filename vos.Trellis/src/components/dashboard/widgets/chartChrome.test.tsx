import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { SeriesTable } from './chartChrome';

describe('SeriesTable', () => {
  it('is hidden by a block that clips it, so its width cannot widen the page', () => {
    render(
      <SeriesTable
        caption="Temperature range"
        columns={['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']}
        rows={[{ name: 'Recorded high', cells: Array(12).fill('33 °C') }]}
      />,
    );

    const table = screen.getByRole('table', { name: 'Temperature range' });
    expect(table).not.toHaveClass('sr-only');
    expect(table.parentElement?.tagName).toBe('DIV');
    expect(table.parentElement).toHaveClass('sr-only');
  });
});
