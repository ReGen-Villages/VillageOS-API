/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/** An export nothing imports makes a reader search the whole source to learn that nobody needs it.
 *  Value exports only, because a type is often exported to be named in a signature rather than imported.
 *  A file's own test counts as an importer, or every new component would be reported as it is written.
 *  A finding is silenced only in ALLOWED, with a reason. */

const SOURCE_DIRECTORY = dirname(fileURLToPath(import.meta.url));

const ALLOWED = new Map<string, string>([
  ['useFormOptions', 'kept for the site study\'s public pages, which read the form options from the intake service (VillageOS-API 8190)'],
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
