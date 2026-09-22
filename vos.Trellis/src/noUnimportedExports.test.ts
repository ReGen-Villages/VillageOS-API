/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Guards that nothing is exported from the console source without something importing it.
 *
 * An export says "somewhere else needs this". When nothing does, the word is a claim the code
 * cannot back: a reader weighing a change has to search the whole source to find out that the
 * answer is nobody, and a symbol nothing imports is kept alive by the keyword alone.
 *
 * Value exports only — `function`, `const`, `class`. A type is often exported to be named in a
 * consumer's signature rather than imported by name, so the same rule would misread it.
 *
 * A file's own test counts as an importer: a symbol reached only by the test that proves it is
 * still reached by something, and the alternative flags every component the moment it is written.
 *
 * A finding is silenced only by adding it to ALLOWED with a reason.
 */

const SOURCE_DIRECTORY = dirname(fileURLToPath(import.meta.url));

const ALLOWED = new Map<string, string>([
]);

function sourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) return sourceFiles(path);
    return /\.tsx?$/.test(entry) ? [path] : [];
  });
}

describe('console exports', () => {
  it('are every one of them imported somewhere', () => {
    const files = sourceFiles(SOURCE_DIRECTORY);
    const sources = new Map(files.map((path) => [path, readFileSync(path, 'utf8')]));

    const unimported: string[] = [];
    for (const [path, source] of sources) {
      for (const match of source.matchAll(/^export (?:async )?(?:function|const|class) (\w+)/gm)) {
        const name = match[1];
        if (ALLOWED.has(name)) continue;
        const imported = [...sources].some(
          ([other, text]) => other !== path && new RegExp(`\\b${name}\\b`).test(text),
        );
        if (!imported) unimported.push(`${path.slice(SOURCE_DIRECTORY.length + 1)} :: ${name}`);
      }
    }

    expect(unimported).toEqual([]);
  });

  it('allows a name only with a reason', () => {
    expect([...ALLOWED].filter(([, reason]) => !reason.trim()).map(([name]) => name)).toEqual([]);
  });
});
