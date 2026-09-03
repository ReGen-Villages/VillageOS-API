import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

const SPECIFIER = /(?:from\s*|import\s*\(\s*)['"](\.[^'"]+)['"]/g;

/** What one source file imports, as paths on disk. Serves the guards that judge the client by what a file
 *  reaches rather than by where it sits. A specifier naming anything that is not source — a stylesheet, an
 *  asset, a package — is left out, because nothing further is reached through it. */
export function importsOf(file: string): string[] {
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
