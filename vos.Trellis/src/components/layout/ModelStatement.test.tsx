import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { ConnectionState } from '../../types/connection';

const stream: { connection: ConnectionState } = { connection: 'live' };
vi.mock('../../hooks/useSse', () => ({ useSse: () => ({ connection: stream.connection, on: () => () => {} }) }));

import { useModelStore } from '../../stores/modelStore';
import { useActivityStore } from '../../stores/activityStore';
import { ModelStatement } from './ModelStatement';

const thing = (id: string) => ({ Id: id, Name: id, Properties: {} });
const edge = (id: string) => ({ Id: id, Name: id, SubjectId: 'a', PredicateId: 'is', TargetId: 'b', Properties: {} });

describe('ModelStatement', () => {
  beforeEach(() => {
    stream.connection = 'live';
    useModelStore.setState({ things: [], relationships: [], loaded: false, holdsWholeModel: false });
    useActivityStore.setState({ events: [] });
  });

  it('says the model is still being read until it is loaded, then how much it holds', () => {
    const { rerender } = render(<ModelStatement isCollapsed={false} />);
    expect(screen.getByText('Reading the model…')).toBeInTheDocument();

    useModelStore.setState({ things: [thing('a'), thing('b'), thing('c')], relationships: [edge('r1')], loaded: true, holdsWholeModel: true });
    rerender(<ModelStatement isCollapsed={false} />);
    expect(screen.getByText('3 Things · 1 relationship')).toBeInTheDocument();
  });

  it('counts a page\'s own set as the page\'s, not as the model\'s', () => {
    useModelStore.setState({ things: [thing('a'), thing('b')], relationships: [edge('r1')], loaded: true, holdsWholeModel: false });
    render(<ModelStatement isCollapsed={false} />);

    expect(screen.getByText('On this page: 2 Things · 1 relationship')).toBeInTheDocument();
  });

  it.each([
    ['connecting', 'Connecting…'],
    ['live', 'Live'],
    ['lost', 'Connection lost'],
  ] as const)('says the connection is %s in words', (connection, words) => {
    stream.connection = connection;
    render(<ModelStatement isCollapsed={false} />);
    expect(screen.getByText(words)).toBeInTheDocument();
  });

  it('shows when the newest event arrived, and that nothing has yet', () => {
    const { rerender } = render(<ModelStatement isCollapsed={false} />);
    expect(screen.getByText('Nothing has moved yet')).toBeInTheDocument();

    const at = new Date(2026, 8, 19, 14, 5, 0);
    useActivityStore.setState({ events: [
      { Type: 'PropertyChanged', Timestamp: new Date(2026, 8, 19, 13, 0, 0).toISOString(), Description: 'earlier' },
      { Type: 'PropertyChanged', Timestamp: at.toISOString(), Description: 'newest' },
    ] });
    rerender(<ModelStatement isCollapsed={false} />);
    expect(screen.getByText(`Last moved ${at.toLocaleTimeString()}`)).toBeInTheDocument();
  });

  it('keeps only the live mark when the sidebar is collapsed, with the statement as its title', () => {
    useModelStore.setState({ things: [thing('a')], relationships: [], loaded: true, holdsWholeModel: true });
    render(<ModelStatement isCollapsed />);
    expect(screen.queryByText('1 Thing · 0 relationships')).toBeNull();
    expect(screen.getByTitle('Live · 1 Thing · 0 relationships · Nothing has moved yet')).toBeInTheDocument();
  });
});
