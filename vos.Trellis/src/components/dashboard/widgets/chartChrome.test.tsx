import { describe, it, expect } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
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

  it('shows the same table to every reader when its button is pressed, and hides it again', () => {
    render(<SeriesTable caption="Wind" columns={['Mean']} rows={[{ name: 'Jan', cells: ['12 km/h'] }]} />);
    const button = screen.getByRole('button', { name: 'Table' });
    expect(button).toHaveAttribute('aria-pressed', 'false');

    fireEvent.click(button);

    expect(button).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('table', { name: 'Wind' }).parentElement).not.toHaveClass('sr-only');
    expect(screen.getByText('12 km/h')).toBeVisible();

    fireEvent.click(button);
    expect(screen.getByRole('table', { name: 'Wind' }).parentElement).toHaveClass('sr-only');
  });

  it('builds a table too large to keep hidden only once it is asked for', () => {
    render(<SeriesTable caption="Every day" columns={['00:00']} rows={[{ name: 'Jan 1', cells: ['3'] }]} hiddenUntilShown />);
    expect(screen.queryByRole('table')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Table' }));

    expect(screen.getByRole('table', { name: 'Every day' })).toBeInTheDocument();
  });
});
