import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import type { Binding, ExceptionBucket, ExceptionWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';

const counts = new Map<string, number>();

vi.mock('../../../hooks/useDashboard', () => ({
  useBindings: (bindings: Binding[]) =>
    bindings.map((binding) => ({ loading: false, error: false, value: counts.get(JSON.stringify(binding)) ?? null })),
}));

const { ExceptionBar } = await import('./ExceptionBar');

function bucket(label: string, count: number, severity: ExceptionBucket['severity']): ExceptionBucket {
  const value = { kind: 'count', archetype: 'Spring', state: label } as unknown as Binding;
  counts.set(JSON.stringify(value), count);
  return { label, value, severity };
}

const SPRINGS_BY_FLOW: ExceptionWidget = {
  type: 'exceptionBar',
  title: 'Springs by flow',
  buckets: [bucket('Steady', 5, 'good'), bucket('Falling', 3, 'warning'), bucket('Dry', 2, 'critical')],
};

function segmentFills(container: HTMLElement): string[] {
  return Array.from(container.querySelectorAll<HTMLElement>('div[style*="width"]')).map((segment) => segment.style.background);
}

describe('ExceptionBar', () => {
  it('fills each bucket of the bar with the colour of its severity', () => {
    const { container } = render(<ExceptionBar widget={SPRINGS_BY_FLOW} context={{} as ResolveContext} />);

    expect(segmentFills(container)).toEqual(['var(--good)', 'var(--warn)', 'var(--crit)']);
  });

  it('colours each bucket count by its severity', () => {
    render(<ExceptionBar widget={SPRINGS_BY_FLOW} context={{} as ResolveContext} />);

    expect(screen.getByText('5')).toHaveClass('text-emerald-600');
    expect(screen.getByText('3')).toHaveClass('text-amber-600');
    expect(screen.getByText('2')).toHaveClass('text-red-600');
  });
});
