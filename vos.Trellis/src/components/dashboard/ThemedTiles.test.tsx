import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import type { DashboardSection, DeclaredTheme, Widget } from '../../types/dashboard';
import { buildModelIndex, type ResolveContext } from '../../api/dashboardApi';
import type { ModelReads } from '../../api/modelReads';

// The gallery draws the section's widgets through the one renderer every page draws them with, which is
// proven where it lives. Here a widget only has to be told apart from its neighbours.
vi.mock('./widgets/WidgetRenderer', () => ({
  WidgetRenderer: ({ widget }: { widget: Widget }) => <div data-testid="widget">{widget.title}</div>,
}));

const { ThemedTiles } = await import('./ThemedTiles');

const themes: DeclaredTheme[] = [
  { name: 'Water', colour: '#3B7DE0', icon: 'droplet', order: 2 },
  { name: 'Temperature', colour: '#F0A840', icon: 'thermometer', order: 1 },
  { name: 'Sun', colour: '#FFF0C0', icon: 'sun', order: 4 },
];

const sections: DashboardSection[] = [
  { title: 'Rain', theme: 'Water', widgets: [] },
  {
    title: 'Temperature',
    theme: 'Temperature',
    widgets: [
      { type: 'kpi', title: 'Thermal comfort', value: { kind: 'const', value: 0.276 }, format: 'percent1' },
      { type: 'kpi', title: 'Hot days', value: { kind: 'const', value: 38.6 }, format: 'decimal1', unit: 'days' },
      { type: 'verdict', title: 'Monthly range', rows: [] },
      { type: 'table', title: 'Degree days', columns: [], rows: { kind: 'const', value: 0 } },
    ],
  },
  { title: 'Sunshine', theme: 'Sun', widgets: [{ type: 'verdict', title: 'Sunshine hours', rows: [] }] },
  { title: 'Rock', theme: 'Terrain', widgets: [{ type: 'verdict', title: 'Slope', rows: [] }] },
];

const context: ResolveContext = {
  index: buildModelIndex([], []),
  scopeId: null,
  reads: {} as ModelReads,
};

function draw() {
  render(<ThemedTiles sections={sections} themes={themes} context={context} />);
}

describe('the themed tiles', () => {
  it('draws one tile per themed section, in the order the themes declare, faced in each theme colour', () => {
    draw();

    const tiles = screen.getAllByRole('button', { name: /Temperature|Rain|Sunshine|Rock/ });
    expect(tiles.map((tile) => tile.getAttribute('aria-label'))).toEqual([
      'Temperature', 'Rain', 'Sunshine', 'Rock',
    ]);
    expect(tiles[0]).toHaveStyle({ backgroundColor: '#F0A840' });
  });

  it('shows the section\'s figures while the tile is hovered or focused, and hides them after', async () => {
    draw();
    const tile = screen.getByRole('button', { name: 'Temperature' });

    fireEvent.mouseEnter(tile);
    expect(await screen.findByText('27.6%')).toBeInTheDocument();
    expect(screen.getByText('Thermal comfort')).toBeInTheDocument();
    expect(screen.getByText(/38\.6/)).toBeInTheDocument();

    fireEvent.mouseLeave(tile);
    expect(screen.queryByText('Thermal comfort')).toBeNull();

    fireEvent.focus(tile);
    expect(await screen.findByText('Thermal comfort')).toBeInTheDocument();
  });

  it('reads "not assessed" on a themed section with no widgets, and opens nothing', () => {
    draw();
    const tile = screen.getByRole('button', { name: 'Rain' });

    expect(within(tile).getByText('Not assessed')).toBeInTheDocument();
    fireEvent.click(tile);
    expect(screen.queryByTestId('widget')).toBeNull();
  });

  it('opens a gallery of the section\'s other widgets on click, a card full width, and finds its way back', () => {
    draw();

    fireEvent.click(screen.getByRole('button', { name: 'Temperature' }));
    expect(screen.getAllByTestId('widget').map((card) => card.textContent)).toEqual([
      'Monthly range', 'Degree days',
    ]);
    expect(screen.queryByText('Thermal comfort')).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Open Degree days' }));
    expect(screen.getAllByTestId('widget').map((card) => card.textContent)).toEqual(['Degree days']);

    fireEvent.click(screen.getByRole('button', { name: 'Back to the gallery' }));
    expect(screen.getAllByTestId('widget')).toHaveLength(2);

    fireEvent.click(screen.getByRole('button', { name: 'Back to the tiles' }));
    expect(screen.queryByTestId('widget')).toBeNull();
    expect(screen.getByRole('button', { name: 'Temperature' })).toBeInTheDocument();
  });

  it('still draws a section naming a theme the model does not declare, on a neutral face', () => {
    draw();
    const tile = screen.getByRole('button', { name: 'Rock' });

    expect(tile).not.toHaveStyle({ backgroundColor: '#F0A840' });
    fireEvent.click(tile);
    expect(screen.getByTestId('widget')).toHaveTextContent('Slope');
  });

  it('draws nothing where no section names a theme', () => {
    const { container } = render(
      <ThemedTiles sections={[{ title: 'Balances', widgets: [] }]} themes={themes} context={context} />,
    );

    expect(container).toBeEmptyDOMElement();
  });
});
