// @vitest-environment node
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const packageFolder = fileURLToPath(new URL('..', import.meta.url));

function packedFiles() {
  const pack = spawnSync('npm', ['pack', '--dry-run', '--json', '--ignore-scripts'], { cwd: packageFolder, encoding: 'utf-8' });
  return JSON.parse(pack.stdout)[0].files.map((file) => file.path);
}

describe('what a published version carries', () => {
  const files = packedFiles();

  it('carries the source an application builds from', () => {
    expect(files).toContain('src/index.ts');
  });

  it('carries no test and no test setup', () => {
    expect(files.filter((path) => path.includes('.test.') || path.endsWith('setupTests.ts'))).toEqual([]);
  });

  it('carries nothing of how it is released', () => {
    expect(files.filter((path) => path.startsWith('release/'))).toEqual([]);
  });
});
