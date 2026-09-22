import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { WidgetCard } from './WidgetCard';

describe('WidgetCard', () => {
  it('shows the hint beside the title when the widget states one', () => {
    render(<WidgetCard title="Rows" hint="newest first">body</WidgetCard>);

    expect(screen.getByText('newest first')).toBeInTheDocument();
    expect(screen.getByRole('heading').textContent).toBe('Rowsnewest first');
  });

  it('shows the title alone when it states none', () => {
    render(<WidgetCard title="Rows">body</WidgetCard>);

    expect(screen.getByRole('heading').textContent).toBe('Rows');
  });

  it('stays inside the column it was given', () => {
    const { container } = render(<WidgetCard title="Rows">body</WidgetCard>);

    expect((container.firstElementChild as HTMLElement).className).toContain('min-w-0');
  });
});
