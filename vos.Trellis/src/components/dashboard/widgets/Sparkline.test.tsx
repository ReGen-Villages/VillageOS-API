import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { Sparkline } from './Sparkline';

function lineAt(container: HTMLElement): SVGLineElement | null {
  return container.querySelector('line');
}

describe('Sparkline baseline', () => {
  it('draws no reference line when no baseline is given', () => {
    const { container } = render(<Sparkline values={[1, 5, 3]} />);

    expect(lineAt(container)).toBeNull();
  });

  it('places the reference line at the baseline value', () => {
    const { container } = render(<Sparkline values={[0, 100]} baseline={50} height={40} />);
    const line = lineAt(container)!;

    // Mid-domain baseline sits mid-plot: pad 3, so between y=37 (0) and y=3 (100).
    expect(Number(line.getAttribute('y1'))).toBeCloseTo(20, 5);
    expect(line.getAttribute('y1')).toEqual(line.getAttribute('y2'));
  });

  it('widens the domain so a baseline above every sample stays visible', () => {
    const { container } = render(<Sparkline values={[10, 20]} baseline={100} height={40} />);
    const line = lineAt(container)!;

    expect(Number(line.getAttribute('y1'))).toBeCloseTo(3, 5);
  });
});
