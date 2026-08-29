import { describe, it, expect } from 'vitest';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, relative, resolve } from 'node:path';

/**
 * The public form is served on a site anybody can open, and it holds no credential. Nothing it loads may
 * reach the broker: not to read the model, not to sign in, not to find out whether somebody is signed in.
 * What it needs from the model, the intake service answers.
 *
 * Asserted over the import graph rather than over a directory, because the form shares the wizard, the
 * map and the translations with the signed-in application — sharing a file is the point, and reaching
 * the broker through one is the thing that must not happen.
 */
const SOURCE = resolve(__dirname, '..');
const ENTRY = resolve(__dirname, 'main.tsx');

const BROKER_CLIENT = resolve(SOURCE, 'api/client.ts');
const SIGNED_IN_STATE = resolve(SOURCE, 'hooks/useAuth.ts');

const SPECIFIER = /(?:from\s*|import\s*\(\s*)['"](\.[^'"]+)['"]/g;

function reachedFrom(entry: string): Set<string> {
  const reached = new Set<string>();
  const frontier = [entry];
  while (frontier.length > 0) {
    const file = frontier.pop()!;
    if (reached.has(file)) continue;
    reached.add(file);

    const source = readFileSync(file, 'utf8');
    for (const [, specifier] of source.matchAll(SPECIFIER)) {
      const resolved = fileFor(resolve(dirname(file), specifier));
      if (resolved && !reached.has(resolved)) frontier.push(resolved);
    }
  }
  return reached;
}

/** What a specifier without an extension names on disk, in the order a bundler tries them. Anything that
 *  is not source — a stylesheet, an asset — reaches no further and is left out. */
function fileFor(path: string): string | null {
  for (const candidate of [`${path}.ts`, `${path}.tsx`, `${path}/index.ts`, `${path}/index.tsx`]) {
    if (existsSync(candidate)) return candidate;
  }
  return null;
}

describe('what the public form loads', () => {
  const reached = reachedFrom(ENTRY);
  const named = [...reached].map((file) => relative(SOURCE, file));

  it('reaches the wizard it renders, so an empty walk cannot pass this file', () => {
    expect(named).toContain('intake/IntakeWizard.tsx');
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
