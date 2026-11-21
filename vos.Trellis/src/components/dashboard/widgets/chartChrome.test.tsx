import { describe, it, expect, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { AxisUnit, MonthColumn, SeriesTable, ValueGridlines } from './chartChrome';
import { formatNumber } from './format';

describe('ValueGridlines', () => {
  const drawn = (format?: 'percent') => render(
    <svg><ValueGridlines ticks={[0, 0.5, 1]} y={(value) => 100 - value * 100} left={40} right={300} format={format} /></svg>,
  ).container;

  it('draws a line across the plot at each tick, labelled on the axis', () => {
    const lines = [...drawn().querySelectorAll('line')];

    expect(lines.map((line) => line.getAttribute('y1'))).toEqual(['100', '50', '0']);
    expect(lines.every((line) => line.getAttribute('x1') === '40' && line.getAttribute('x2') === '300')).toBe(true);
  });

  it('labels each tick in the format it is given, whole numbers when none is', () => {
    expect([...drawn().querySelectorAll('text')].map((text) => text.textContent))
      .toEqual([0, 0.5, 1].map((tick) => formatNumber(tick, 'integer')));
    expect([...drawn('percent').querySelectorAll('text')].map((text) => text.textContent))
      .toEqual([0, 0.5, 1].map((tick) => formatNumber(tick, 'percent')));
  });
});

describe('MonthColumn', () => {
  const column = (active: boolean, onActive = vi.fn()) => render(
    <svg>
      <MonthColumn monthKey="7" centre={100} markWidth={20} plotTop={8} plotBottom={228} name="Jul" readOut="Jul: 31 °C"
                   active={active} onActive={onActive}>
        <rect data-mark="bar" />
      </MonthColumn>
    </svg>,
  ).container;

  it('reads as one image named by its read-out, with its marks and its name under the axis', () => {
    const month = column(false).querySelector('[data-month="7"]')!;

    expect(month).toHaveAttribute('role', 'img');
    expect(month).toHaveAttribute('aria-label', 'Jul: 31 °C');
    expect(month.querySelector('[data-mark="bar"]')).not.toBeNull();
    expect(month).toHaveTextContent('Jul');
  });

  it('is active while focused or under the pointer, and outlined only while active', () => {
    const onActive = vi.fn();
    const month = column(false, onActive).querySelector('[data-month="7"]')!;

    fireEvent.focus(month);
    fireEvent.blur(month);
    fireEvent.mouseEnter(month);
    fireEvent.mouseLeave(month);

    expect(onActive.mock.calls).toEqual([[true], [false], [true], [false]]);
    expect(month.querySelectorAll('rect[fill="none"]')).toHaveLength(0);
    expect(column(true).querySelectorAll('rect[fill="none"]')).toHaveLength(1);
  });
});

describe('AxisUnit', () => {
  it('writes the unit under the value axis', () => {
    const { container } = render(<svg><AxisUnit unit="°C" left={40} plotBottom={228} /></svg>);

    expect(container.querySelector('text')).toHaveTextContent('°C');
    expect(container.querySelector('text')!.getAttribute('y')).toBe('243');
  });

  it('writes nothing for a chart with no unit', () => {
    const { container } = render(<svg><AxisUnit unit={undefined} left={40} plotBottom={228} /></svg>);

    expect(container.querySelector('text')).toBeNull();
  });
});

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
