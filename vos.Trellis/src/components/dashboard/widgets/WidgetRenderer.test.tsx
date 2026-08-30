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
    draw({ type: 'kpi', title: 'Throughput', value: { kind: 'const', value: 1 } });

    expect(screen.queryByText('Widget not drawn')).toBeNull();
    expect(screen.getByText('Throughput')).toBeInTheDocument();
  });
});

// A widget whose binding this build cannot answer is refused rather than drawn, because drawing it
// puts a figure on screen that reads as an answer (Bug #6866).
describe('a widget asking for binding vocabulary this build cannot answer', () => {
  it('names the binding kind it could not answer', () => {
    draw({ type: 'kpi', title: 'Throughput', value: { kind: 'runningTotal', property: 'volume' } });

    expect(screen.getByText(/runningTotal/)).toBeInTheDocument();
  });

  it('names a field the binding kind does not read', () => {
    draw({
      type: 'kpi', title: 'Throughput',
      value: {
        kind: 'timeseries', archetype: 'Reading', happenedAt: 'recordedAt', op: 'sum',
        property: 'volume', bucketSeconds: 900, buckets: 32, smoothing: 'exponential',
      },
    });

    expect(screen.getByText(/smoothing/)).toBeInTheDocument();
  });

  // The figure is the defect: a widget drawn from a question only half understood shows a number in
  // the right units and the wrong size, which reads as an answer rather than as a gap.
  it('does not draw the widget it refused', () => {
    draw({ type: 'kpi', title: 'Throughput', value: { kind: 'runningTotal', property: 'volume' } });

    expect(screen.queryByText('Throughput')).toBeNull();
  });

  // A spec can leave the word out as easily as misspell it, and the notice is a translated sentence
  // either way — so the gap where the word would be is filled in the reader's own language.
  it('says the kind was not given where the spec names none', () => {
    draw({ type: 'kpi', title: 'Throughput', value: { property: 'volume' } });

    expect(screen.getByText(/none given/)).toBeInTheDocument();
  });

  it('draws a widget whose every binding it can answer', () => {
    draw({
      type: 'kpi', title: 'Throughput',
      value: {
        kind: 'timeseries', archetype: 'Reading', happenedAt: 'recordedAt', op: 'sum',
        property: 'volume', bucketSeconds: 900, buckets: 32, bucketsPerPoint: 4,
      },
    });

    expect(screen.queryByText('Widget not drawn')).toBeNull();
    expect(screen.getByText('Throughput')).toBeInTheDocument();
  });
});
