import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import { useElementWidth } from './useElementWidth';

const observed: HTMLElement[] = [];
let report: ((width: number) => void) | null = null;

class RecordingResizeObserver {
  private callback: ResizeObserverCallback;
  constructor(callback: ResizeObserverCallback) {
    this.callback = callback;
  }
  observe(element: HTMLElement) {
    observed.push(element);
    report = (width) =>
      this.callback([{ contentRect: { width } } as ResizeObserverEntry], this as unknown as ResizeObserver);
  }
  unobserve() {}
  disconnect() {
    report = null;
  }
}

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
  const original = globalThis.ResizeObserver;

  beforeEach(() => {
    observed.length = 0;
    globalThis.ResizeObserver = RecordingResizeObserver as unknown as typeof ResizeObserver;
  });

  afterEach(() => {
    globalThis.ResizeObserver = original;
    vi.restoreAllMocks();
  });

  it('measures an element that only mounts after the first render', () => {
    const { rerender } = render(<LateChart mounted={false} />);
    expect(observed).toHaveLength(0);

    rerender(<LateChart mounted={true} />);

    expect(observed).toHaveLength(1);
    act(() => report!(704));
    expect(screen.getByTestId('width').textContent).toBe('704');
  });
});
