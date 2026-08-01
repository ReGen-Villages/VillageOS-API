import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import { installResizeObserverDouble } from '../testResizeObserver';
import { useElementHeight } from './useElementHeight';

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
  let observer: ReturnType<typeof installResizeObserverDouble>;

  beforeEach(() => {
    observer = installResizeObserverDouble();
  });

  afterEach(() => {
    observer.restore();
  });

  it('reports the element height, padding and border included', () => {
    render(<MeasuredHeader />);
    expect(screen.getByTestId('height').textContent).toBe('0');

    act(() => observer.report({ height: 48 }));

    expect(screen.getByTestId('height').textContent).toBe('48');
  });
});
