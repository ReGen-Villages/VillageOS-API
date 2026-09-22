import { describe, it, expect, vi } from 'vitest';
import { render } from '@testing-library/react';
import type { DashboardSection, Widget } from '../../types/dashboard';
import type { ResolveContext } from '../../api/dashboardApi';

vi.mock('./widgets/WidgetRenderer', () => ({
  WidgetRenderer: ({ widget }: { widget: Widget }) => <div data-testid="widget">{widget.title}</div>,
}));

const { DashboardSections } = await import('./DashboardSections');

const figure = (title: string, placement?: Widget['placement']): Widget =>
  ({ type: 'kpi', title, value: { kind: 'const', value: 1 }, placement }) as Widget;

function draw(section: DashboardSection, isWide: boolean) {
  const { container } = render(
    <DashboardSections sections={[section]} context={{} as ResolveContext} isWide={isWide} whenEmpty={null} />,
  );
  const grid = container.querySelector('.grid') as HTMLElement;
  return { grid, cells: [...grid.children] as HTMLElement[] };
}

describe('a grid section', () => {
  const section: DashboardSection = {
    layout: 'grid',
    widgets: [figure('right', { column: 6, row: 0, width: 6, height: 2 }), figure('left', { column: 0, row: 0, width: 6, height: 3 })],
  };

  it('places each widget by its column span and row span on twelve columns', () => {
    const { grid, cells } = draw(section, true);
    expect(grid.style.gridTemplateColumns).toBe('repeat(12, minmax(0, 1fr))');
    expect(grid.style.gridAutoRows).toBe('40px');
    expect(cells[0].style.gridColumn).toBe('7 / span 6');
    expect(cells[1].style.gridRow).toBe('1 / span 3');
  });

  it('stacks in reading order when the screen is narrow', () => {
    const { grid, cells } = draw(section, false);
    expect(grid.style.gridTemplateColumns).toBe('minmax(0, 1fr)');
    expect(cells[0].style.order).toBe('1');
    expect(cells[1].style.order).toBe('0');
  });
});

describe('a strip', () => {
  it('draws as many columns as it has figures, up to four', () => {
    const { grid } = draw({ widgets: [figure('a'), figure('b')] }, true);
    expect(grid.style.gridTemplateColumns).toBe('repeat(2, minmax(0, 1fr))');
  });
});
