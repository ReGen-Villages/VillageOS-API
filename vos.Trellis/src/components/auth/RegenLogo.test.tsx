import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { RegenLogo } from './RegenLogo';

describe('RegenLogo', () => {
  it('renders an SVG element', () => {
    const { container } = render(<RegenLogo />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg?.getAttribute('viewBox')).toBe('36 34 128 128');
  });

  it('passes className to the SVG element', () => {
    const { container } = render(<RegenLogo className="w-32 h-32" />);
    const svg = container.querySelector('svg');
    expect(svg?.classList.contains('w-32')).toBe(true);
    expect(svg?.classList.contains('h-32')).toBe(true);
  });

  it('contains all four quadrant icon groups', () => {
    const { container } = render(<RegenLogo />);
    // Circle + 2 cross lines = 3 structural elements
    const circles = container.querySelectorAll('svg > g > circle');
    expect(circles.length).toBeGreaterThanOrEqual(1); // outer circle

    // House roof path exists
    const paths = container.querySelectorAll('path');
    expect(paths.length).toBeGreaterThanOrEqual(10); // house, windmill, cloud, circuit, plant

    // Circuit nodes (4) + windmill hub (1) = 5 filled circles
    const filledCircles = container.querySelectorAll('g[fill] circle');
    expect(filledCircles.length).toBeGreaterThanOrEqual(4);
  });

  it('includes draw-on animation CSS class on structural elements', () => {
    const { container } = render(<RegenLogo />);
    const drawElements = container.querySelectorAll('.rd');
    expect(drawElements.length).toBeGreaterThan(0);
  });
});
