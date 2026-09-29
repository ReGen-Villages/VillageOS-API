/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * The shell paints the theme on a container sized to the window. Anything drawn past it — a sidebar
 * taller than the window, or Safari's overscroll — lands on the document's own background, which is
 * white unless a rule paints it. jsdom lays nothing out, so this reads the rules the shell is drawn with.
 */

const SOURCE_DIRECTORY = dirname(fileURLToPath(import.meta.url));
const read = (path: string) => readFileSync(join(SOURCE_DIRECTORY, path), 'utf8');

function shellBackgroundClasses(): string[] {
  const shell = read('components/layout/AppLayout.tsx').match(/className="([^"]*h-dvh[^"]*)"/);
  return (shell ? shell[1] : '').split(/\s+/).filter((name) => /^(dark:)?bg-/.test(name));
}

function documentRootRule(): string {
  const stylesheet = read('index.css');
  const start = stylesheet.search(/^html\s*\{/m);
  return start < 0 ? '' : stylesheet.slice(start, stylesheet.indexOf('}', start));
}

function navigationClasses(): string[] {
  const navigation = read('components/layout/Sidebar.tsx').match(/<nav className="([^"]*)"/);
  return (navigation ? navigation[1] : '').split(/\s+/);
}

describe('the console below the themed shell', () => {
  it('paints the document root with the same background as the shell, in both themes', () => {
    const shell = shellBackgroundClasses();

    expect(shell).toHaveLength(2);
    for (const name of shell) expect(documentRootRule()).toContain(name);
  });

  it('scrolls the sidebar navigation inside the sidebar instead of pushing its footer past the shell', () => {
    expect(navigationClasses()).toContain('overflow-y-auto');
  });
});
