import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { EngineMetricsPanel } from './EngineMetricsPanel';
import type { EngineMetricsSummary } from '../../types/engineMetrics';

function summary(): EngineMetricsSummary {
  return {
    ModelId: 'm1',
    ModelName: 'TestModel',
    Ranges: {
      RegisteredRanges: 12,
      RangesWithBindings: 3,
      PropertyDependencyEdges: 20,
      StateDependencyEdges: 4,
      BindingDependencyEdges: 6,
      DependencyEdges: 30,
      EstimatedBytes: 17088,
    },
    Rollups: {
      ThingsOwningRollups: 2,
      RollupProperties: 5,
      MemberEdges: 40,
      EstimatedBytes: 5120,
    },
    EstimatedBytesTotal: 22208,
  };
}

describe('EngineMetricsPanel', () => {
  it('shows both engines with reactor counts, edges, and estimated memory', () => {
    render(<EngineMetricsPanel metrics={summary()} />);

    expect(screen.getByText('Reactive engines')).toBeInTheDocument();
    expect(screen.getByText('Range evaluation')).toBeInTheDocument();
    expect(screen.getByText('Reactive computation')).toBeInTheDocument();
    expect(screen.getByText('12')).toBeInTheDocument(); // registered ranges
    expect(screen.getByText('30')).toBeInTheDocument(); // dependency edges
    expect(screen.getByText('5')).toBeInTheDocument(); // roll-up definitions
    expect(screen.getByText('40')).toBeInTheDocument(); // member edges
    expect(screen.getByText('16.7 KB')).toBeInTheDocument();
    expect(screen.getByText('5.0 KB')).toBeInTheDocument();
    expect(screen.getByText('21.7 KB')).toBeInTheDocument(); // combined total
  });

  it('shows an unavailable placeholder instead of zeros when nothing has loaded', () => {
    render(<EngineMetricsPanel metrics={null} />);

    expect(screen.getByText('Reactive engines')).toBeInTheDocument();
    expect(screen.getByText('Metrics unavailable')).toBeInTheDocument();
    expect(screen.queryByText('0 B')).not.toBeInTheDocument();
  });
});
