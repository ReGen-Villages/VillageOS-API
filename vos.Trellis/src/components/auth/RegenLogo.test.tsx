import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { RegenLogo } from './RegenLogo';
import animatedLogo from './regenLogoAnimated.svg';
import stillLogo from './regenLogoStill.svg';
import animatedSource from './regenLogoAnimated.svg?raw';
import stillSource from './regenLogoStill.svg?raw';

describe('RegenLogo', () => {
  it('animates, and gives the still logo to a reader who asked for less motion', () => {
    const { container } = render(<RegenLogo />);
    const source = container.querySelector('picture source');
    expect(source?.getAttribute('media')).toBe('(prefers-reduced-motion: reduce)');
    expect(source?.getAttribute('srcset')).toBe(stillLogo);
    expect(container.querySelector('picture img')?.getAttribute('src')).toBe(animatedLogo);
  });

  it('passes className to the image', () => {
    const { container } = render(<RegenLogo className="w-32 h-32" />);
    const image = container.querySelector('img');
    expect(image?.classList.contains('w-32')).toBe(true);
    expect(image?.classList.contains('h-32')).toBe(true);
  });

  it('names itself for a reader who cannot see it', () => {
    const { container } = render(<RegenLogo />);
    expect(container.querySelector('img')?.getAttribute('alt')).toBe('ReGen Villages');
  });
});

describe('the logo files', () => {
  it('draw in the brand colour on the same canvas, so swapping one for the other never moves the logo', () => {
    for (const source of [animatedSource, stillSource]) {
      expect(source).toContain('stroke="#37c2aa"');
      expect(source).toMatch(/<svg [^>]*viewBox="128 131 342 342"/);
    }
  });

  it('animates the lines drawing on and keeps the windmill turning without end', () => {
    expect(animatedSource).toContain('attributeName="stroke-dashoffset"');
    expect(animatedSource).toMatch(/type="rotate"[^>]*repeatCount="indefinite"/);
  });

  it('holds no animation in the still file', () => {
    expect(stillSource).not.toMatch(/<animate|<set /);
  });

  it('never switches a dash off by setting it to 0, which Safari draws as no line at all', () => {
    expect(animatedSource).not.toMatch(/attributeName="stroke-dasharray"[^>]*to="0"/);
  });
});
