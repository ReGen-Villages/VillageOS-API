import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { Widget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';

vi.mock('../../../hooks/useDashboard', () => ({
  useBinding: () => ({ loading: false, error: false, value: null }),
  useBindings: (bindings: unknown[]) => bindings.map(() => ({ loading: false, error: false, value: null })),
}));

const { WidgetRenderer } = await import('./WidgetRenderer');

function draw(widget: unknown) {
  render(<WidgetRenderer widget={widget as Widget} ctx={{} as ResolveContext} />);
}

describe('a widget kind this build has no drawing for', () => {
  it('names the kind it could not draw', () => {
    draw({ type: 'sankey', title: 'Flows' });

    expect(screen.getByText(/sankey/)).toBeInTheDocument();
  });

  it('says so even where the spec names no kind at all', () => {
    draw({ title: 'Flows' });

    expect(screen.getByText('Widget not drawn')).toBeInTheDocument();
  });

  it('draws a kind it does know without the notice', () => {
    draw({ type: 'kpi', title: 'Throughput', value: { kind: 'constant', value: 1 } });

    expect(screen.queryByText('Widget not drawn')).toBeNull();
    expect(screen.getByText('Throughput')).toBeInTheDocument();
  });
});
