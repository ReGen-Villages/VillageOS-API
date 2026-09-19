import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen } from '@testing-library/react';

const stream = { connected: true };
vi.mock('../../hooks/useSse', () => ({ useSse: () => ({ connected: stream.connected, on: () => () => {} }) }));

import { useModelStore } from '../../stores/modelStore';
import { useActivityStore } from '../../stores/activityStore';
import { ModelStatement } from './ModelStatement';

const thing = (id: string) => ({ Id: id, Name: id, Properties: {} });
const edge = (id: string) => ({ Id: id, Name: id, SubjectId: 'a', PredicateId: 'is', TargetId: 'b', Properties: {} });

describe('ModelStatement', () => {
  beforeEach(() => {
    stream.connected = true;
    useModelStore.setState({ things: [], relationships: [], loaded: false });
    useActivityStore.setState({ events: [] });
  });

  it('says the model is still being read until it is loaded, then how much it holds', () => {
    const { rerender } = render(<ModelStatement isCollapsed={false} />);
    expect(screen.getByText('Reading the model…')).toBeInTheDocument();

    useModelStore.setState({ things: [thing('a'), thing('b'), thing('c')], relationships: [edge('r1')], loaded: true });
    rerender(<ModelStatement isCollapsed={false} />);
    expect(screen.getByText('3 Things · 1 relationship')).toBeInTheDocument();
  });

  it('follows the connection flag between live and not live', () => {
    const { rerender } = render(<ModelStatement isCollapsed={false} />);
    expect(screen.getByText('Live')).toBeInTheDocument();

    stream.connected = false;
    rerender(<ModelStatement isCollapsed={false} />);
    expect(screen.getByText('Not live')).toBeInTheDocument();
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
    useModelStore.setState({ things: [thing('a')], relationships: [], loaded: true });
    render(<ModelStatement isCollapsed />);
    expect(screen.queryByText('1 Thing · 0 relationships')).toBeNull();
    expect(screen.getByTitle('Live · 1 Thing · 0 relationships · Nothing has moved yet')).toBeInTheDocument();
  });
});
