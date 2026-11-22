import { describe, it, expect } from 'vitest';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, relative, resolve } from 'node:path';

/**
 * The public pages are served on a site anybody can open, and they hold no credential. Nothing either
 * loads may reach the broker: not to read the model, not to sign in, not to find out whether somebody is
 * signed in. What they need from the model, the intake service answers.
 *
 * Asserted over the import graph rather than over a directory, because they share files with the
 * signed-in application — sharing a file is the point, and reaching the broker through one is the thing
 * that must not happen.
 *
 * The pages are read out of `vite.public.config.ts` and each page's own module scripts, so a page added
 * to that build is held to the rule with nothing to add here.
 */
const TRELLIS = resolve(__dirname, '../..');
const SOURCE = resolve(TRELLIS, 'src');

const PAGES = [
  ...readFileSync(resolve(TRELLIS, 'vite.public.config.ts'), 'utf8').matchAll(/resolve\(__dirname, '([^']+\.html)'\)/g),
].map(([, page]) => page);

const MODULE_SCRIPT = /<script\s+type="module"\s+src="\/([^"]+)"/g;

function scriptsOf(page: string): string[] {
  return [...readFileSync(resolve(TRELLIS, page), 'utf8').matchAll(MODULE_SCRIPT)].map(([, script]) =>
    resolve(TRELLIS, script),
  );
}

const BROKER_CLIENT = resolve(SOURCE, 'api/client.ts');
const SIGNED_IN_STATE = resolve(SOURCE, 'hooks/useAuth.ts');

const SPECIFIER = /(?:from\s*|import\s*\(\s*)['"](\.[^'"]+)['"]/g;

/** What one source file imports, as paths on disk. A specifier naming anything that is not source — a
 *  stylesheet, an asset, a package — is left out, because nothing further is reached through it. */
function importsOf(file: string): string[] {
  const source = readFileSync(file, 'utf8');
  return [...source.matchAll(SPECIFIER)]
    .map(([, specifier]) => fileFor(resolve(dirname(file), specifier)))
    .filter((imported): imported is string => imported !== null);
}

/** What a specifier without an extension names on disk, in the order a bundler tries them. */
function fileFor(path: string): string | null {
  for (const candidate of [`${path}.ts`, `${path}.tsx`, `${path}/index.ts`, `${path}/index.tsx`]) {
    if (existsSync(candidate)) return candidate;
  }
  return null;
}

function reachedFrom(entry: string): Set<string> {
  const reached = new Set<string>();
  const frontier = [entry];
  while (frontier.length > 0) {
    const file = frontier.pop()!;
    if (reached.has(file)) continue;
    reached.add(file);

    for (const imported of importsOf(file)) {
      if (!reached.has(imported)) frontier.push(imported);
    }
  }
  return reached;
}

describe('the public build', () => {
  it('names its pages in the form this file reads, so a reworded config cannot leave every page unchecked', () => {
    expect(PAGES).not.toEqual([]);
  });

  it('serves the site study, loading its one entry', () => {
    expect(PAGES).toEqual(['public-study.html']);
    expect(scriptsOf('public-study.html')).toEqual([resolve(SOURCE, 'study/main.tsx')]);
  });
});

const ENTRIES = PAGES.flatMap((page) => scriptsOf(page).map((script) => [page, script] as const));

describe.each(ENTRIES)('what %s loads through %s', (_page, entry) => {
  const reached = reachedFrom(entry);
  const named = [...reached].map((file) => relative(SOURCE, file));

  it('reaches a component, so an empty walk cannot pass this file', () => {
    expect(named.some((file) => file.endsWith('.tsx') && file !== relative(SOURCE, entry))).toBe(true);
  });

  it('reaches neither the broker client nor the signed-in state', () => {
    expect(reached.has(BROKER_CLIENT)).toBe(false);
    expect(reached.has(SIGNED_IN_STATE)).toBe(false);
  });

  it('reaches none of the signed-in application: no page, no shell, no sign-in', () => {
    const signedIn = named.filter(
      (file) =>
        file.startsWith('pages/') ||
        file.startsWith('components/auth/') ||
        file.startsWith('components/layout/') ||
        file === 'App.tsx',
    );
    expect(signedIn).toEqual([]);
  });
});
