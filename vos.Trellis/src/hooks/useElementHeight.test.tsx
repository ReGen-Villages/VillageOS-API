import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import { useElementHeight } from './useElementHeight';

let report: ((height: number) => void) | null = null;

class RecordingResizeObserver {
  private callback: ResizeObserverCallback;
  constructor(callback: ResizeObserverCallback) {
    this.callback = callback;
  }
  observe(element: HTMLElement) {
    report = (height) => {
      element.getBoundingClientRect = () => ({ height }) as DOMRect;
      this.callback([{ target: element } as unknown as ResizeObserverEntry], this as unknown as ResizeObserver);
    };
  }
  unobserve() {}
  disconnect() {
    report = null;
  }
}

function MeasuredHeader() {
  const [ref, height] = useElementHeight();
  return (
    <div>
      <span data-testid="height">{height}</span>
      <div ref={ref}>header</div>
    </div>
  );
}

describe('useElementHeight', () => {
  const original = globalThis.ResizeObserver;

  beforeEach(() => {
    globalThis.ResizeObserver = RecordingResizeObserver as unknown as typeof ResizeObserver;
  });

  afterEach(() => {
    globalThis.ResizeObserver = original;
  });

  it('reports the element height, padding and border included', () => {
    render(<MeasuredHeader />);
    expect(screen.getByTestId('height').textContent).toBe('0');

    act(() => report!(48));

    expect(screen.getByTestId('height').textContent).toBe('48');
  });
});
