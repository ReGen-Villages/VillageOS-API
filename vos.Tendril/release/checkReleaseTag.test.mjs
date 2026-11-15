// @vitest-environment node
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';

const script = new URL('./checkReleaseTag.mjs', import.meta.url).pathname;
const { version } = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf-8'));

function check(reference, onDevelop) {
  return spawnSync(process.execPath, [script, reference, onDevelop], { encoding: 'utf-8' });
}

describe('checking a release tag in the build', () => {
  it('passes the ref of a tag naming the package version on develop', () => {
    const run = check(`refs/tags/tendril-v${version}`, 'true');
    expect(run.status).toBe(0);
    expect(run.stdout).toContain(`Publishing Tendril ${version}.`);
  });

  it('fails the step and says why for a tag naming another version', () => {
    const run = check('refs/tags/tendril-v99.0.0', 'true');
    expect(run.status).toBe(1);
    expect(run.stderr).toContain(`does not name Tendril's version ${version}`);
  });

  it('fails the step when the commit is not on develop', () => {
    const run = check(`refs/tags/tendril-v${version}`, 'false');
    expect(run.status).toBe(1);
  });
});
