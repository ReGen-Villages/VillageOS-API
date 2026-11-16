// @vitest-environment node
import { spawnSync } from 'node:child_process';
import { chmodSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

const script = fileURLToPath(new URL('./publishUnlessHeld.mjs', import.meta.url));
const { name, version } = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf-8'));

function publishWith({ viewStatus, viewAnswer, publishStatus = 0 }) {
  const folder = mkdtempSync(join(tmpdir(), 'tendril-release-'));
  const calls = join(folder, 'calls');
  writeFileSync(calls, '');
  const npm = join(folder, 'npm');
  writeFileSync(npm, [
    '#!/bin/sh',
    `echo "$@" >> '${calls}'`,
    `if [ "$1" = view ]; then printf '%s' '${viewAnswer}'; exit ${viewStatus}; fi`,
    `exit ${publishStatus}`,
  ].join('\n'));
  chmodSync(npm, 0o755);

  const run = spawnSync(process.execPath, [script, '/agent/temp/tendril.npmrc'], {
    encoding: 'utf-8',
    env: { ...process.env, PATH: `${folder}:${process.env.PATH}` },
  });
  return { run, calls: readFileSync(calls, 'utf-8').trim().split('\n').filter(Boolean) };
}

describe('publishing Tendril from develop', () => {
  it('asks the feed for this version through the pipeline sign-in', () => {
    const { calls } = publishWith({ viewStatus: 0, viewAnswer: `"${version}"` });

    expect(calls[0]).toBe(`view ${name}@${version} version --json --userconfig /agent/temp/tendril.npmrc`);
  });

  it('publishes nothing when the feed already holds the version', () => {
    const { run, calls } = publishWith({ viewStatus: 0, viewAnswer: `"${version}"` });

    expect(run.status).toBe(0);
    expect(calls).toHaveLength(1);
    expect(run.stdout).toContain(`The feed already holds Tendril ${version}.`);
  });

  it('publishes when the feed does not hold the version', () => {
    const { run, calls } = publishWith({ viewStatus: 1, viewAnswer: '{"error":{"code":"E404"}}' });

    expect(run.status).toBe(0);
    expect(calls[1]).toBe('publish --userconfig /agent/temp/tendril.npmrc');
  });

  it('fails the step when the upload fails', () => {
    const { run } = publishWith({ viewStatus: 1, viewAnswer: '{"error":{"code":"E404"}}', publishStatus: 1 });

    expect(run.status).toBe(1);
  });

  it('fails the step and publishes nothing when the feed could not be asked', () => {
    const { run, calls } = publishWith({ viewStatus: 1, viewAnswer: '{"error":{"code":"E401"}}' });

    expect(run.status).toBe(1);
    expect(calls).toHaveLength(1);
    expect(run.stderr).toContain('E401');
  });
});
