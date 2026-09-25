/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Fixed-width columns beside a canvas leave it a sliver on a phone, so below a tablet's width each editor
 * panel takes the full width and stacks above or below the canvas. jsdom lays nothing out, so this reads
 * the classes the editors are drawn with.
 */

const SOURCE_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const read = (path: string) => readFileSync(join(SOURCE_DIRECTORY, path), 'utf8');

function classesOfFirstFixedWidthElement(path: string): string {
  const match = read(path).match(/className="([^"]*flex-shrink-0[^"]*)"/);
  return match ? match[1] : '';
}

const PANELS = [
  'components/design/DesignPagesPanel.tsx',
  'components/design/DesignPalette.tsx',
  'components/design/DesignPropertiesPanel.tsx',
  'components/design/TranslationsPanel.tsx',
];

describe('the editors on a screen narrower than a tablet', () => {
  it.each(PANELS)('%s takes the full width and a bounded height, and becomes a column from a tablet up', (path) => {
    const classes = classesOfFirstFixedWidthElement(path).split(/\s+/);

    expect(classes).toContain('w-full');
    expect(classes.some((name) => /^md:w-/.test(name))).toBe(true);
    expect(classes.some((name) => /^max-h-/.test(name))).toBe(true);
    expect(classes).toContain('md:max-h-none');
  });

  it.each(['pages/DesignPage.tsx', 'pages/PipelinePage.tsx'])('%s stacks its panels and canvas, and lays them in a row from a tablet up', (path) => {
    const classLists = [...read(path).matchAll(/className="([^"]*)"/g)].map((match) => match[1].split(/\s+/));

    expect(classLists.some((names) => names.includes('flex-col') && names.includes('md:flex-row'))).toBe(true);
  });

  it('wraps the pipeline toolbar, so the name box is not pushed past the edge', () => {
    expect(read('pages/PipelinePage.tsx')).toContain('<div className="flex flex-wrap items-center gap-2 p-2 border-b');
  });
});
