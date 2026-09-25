/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/** jsdom lays nothing out, so this reads the rule `index.css` states for a phone-sized screen. */

const stylesheet = readFileSync(join(dirname(fileURLToPath(import.meta.url)), 'index.css'), 'utf8');

function blockUnder(mediaQuery: string): string | null {
  const start = stylesheet.indexOf(`@media ${mediaQuery}`);
  if (start < 0) return null;
  const end = stylesheet.indexOf('\n}', start);
  return stylesheet.slice(start, end);
}

describe('form fields on a phone-sized screen', () => {
  it('are at least 16 pixels, so tapping one does not zoom the page', () => {
    const phone = blockUnder('(max-width: 639px)');

    expect(phone).not.toBeNull();
    for (const field of ['input', 'select', 'textarea']) expect(phone).toContain(field);
    expect(phone).toMatch(/font-size:\s*16px/);
  });
});
