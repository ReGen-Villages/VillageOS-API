import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import { installResizeObserverDouble } from '../testResizeObserver';
import { useElementWidth } from './useElementWidth';

/** The chart a KpiCard measures only appears once its series has resolved. */
function LateChart({ mounted }: { mounted: boolean }) {
  const [ref, width] = useElementWidth();
  return (
    <div>
      <span data-testid="width">{width}</span>
      {mounted && <div ref={ref}>chart</div>}
    </div>
  );
}

describe('useElementWidth', () => {
  let observer: ReturnType<typeof installResizeObserverDouble>;

  beforeEach(() => {
    observer = installResizeObserverDouble();
  });

  afterEach(() => {
    observer.restore();
    vi.restoreAllMocks();
  });

  it('measures an element that only mounts after the first render', () => {
    const { rerender } = render(<LateChart mounted={false} />);
    expect(observer.observed).toHaveLength(0);

    rerender(<LateChart mounted={true} />);

    expect(observer.observed).toHaveLength(1);
    act(() => observer.report({ width: 704 }));
    expect(screen.getByTestId('width').textContent).toBe('704');
  });
});
