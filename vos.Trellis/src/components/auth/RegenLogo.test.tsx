import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { RegenLogo } from './RegenLogo';
import { regenMark } from './regenMark';

describe('RegenLogo', () => {
  it('renders an SVG carrying the artwork it was traced from', () => {
    const { container } = render(<RegenLogo />);
    const svg = container.querySelector('svg');
    expect(svg).not.toBeNull();
    expect(svg?.getAttribute('viewBox')).toBe(regenMark.viewBox);
  });

  it('passes className to the SVG element', () => {
    const { container } = render(<RegenLogo className="w-32 h-32" />);
    const svg = container.querySelector('svg');
    expect(svg?.classList.contains('w-32')).toBe(true);
    expect(svg?.classList.contains('h-32')).toBe(true);
  });

  it('draws every outline the artwork holds, and nothing of its own', () => {
    const { container } = render(<RegenLogo />);
    const drawn = [...container.querySelectorAll('path')].map((p) => p.getAttribute('d'));
    expect(drawn).toEqual([...regenMark.paths]);
  });

  it('fills the mark in the brand colour', () => {
    const { container } = render(<RegenLogo />);
    expect(container.querySelector('g')?.getAttribute('fill')).toBe('#37c2aa');
  });

  it('names itself for a reader who cannot see it', () => {
    const { container } = render(<RegenLogo />);
    const svg = container.querySelector('svg');
    expect(svg?.getAttribute('role')).toBe('img');
    expect(svg?.getAttribute('aria-label')).toBe('ReGen Villages');
  });

  it('blooms into place, and holds still for a reader who asked for less motion', () => {
    const { container } = render(<RegenLogo />);
    const css = container.querySelector('style')?.textContent ?? '';
    expect(css).toContain('@keyframes regen-bloom');
    expect(css).toContain('prefers-reduced-motion: reduce');
    expect(container.querySelector('.regen-mark')).not.toBeNull();
  });

  it('keeps the artwork placed once the bloom has ended: the group that animates carries no transform of its own', () => {
    // A CSS transform overrides an SVG transform attribute on the same element, and the bloom's
    // last keyframe is kept after it ends — so a placing transform on the animated group is lost
    // the moment the bloom finishes, and the mark is drawn ten times too large and off the canvas.
    const { container } = render(<RegenLogo />);
    const blooming = container.querySelector('.regen-mark')!;
    expect(blooming.getAttribute('transform')).toBeNull();
    const placed = blooming.closest(`[transform="${regenMark.transform}"]`);
    expect(placed).not.toBeNull();
    expect(placed!.querySelectorAll('path')).toHaveLength(regenMark.paths.length);
  });
});

describe('regenMark', () => {
  it('is a trace of the artwork rather than a handful of hand-written shapes', () => {
    // The drawing is one connected outline — the circle, its two rules and every icon touching them
    // — plus the three dots that touch nothing. A hand-written mark would be many short shapes.
    expect(regenMark.paths).toHaveLength(4);
    for (const d of regenMark.paths) expect(d.startsWith('M')).toBe(true);
    const longest = Math.max(...regenMark.paths.map((d) => d.length));
    expect(longest).toBeGreaterThan(4000);
  });

  it('places the trace with the transform potrace emitted beside it', () => {
    expect(regenMark.transform).toMatch(/^translate\(-?[\d.]+,-?[\d.]+\) scale\(-?[\d.]+,-?[\d.]+\)$/);
  });
});
