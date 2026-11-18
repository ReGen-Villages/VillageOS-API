// @vitest-environment node
import { describe, it, expect } from 'vitest';
import { build } from 'esbuild';

/** Every file a bundler reaches from the reader entry, by the path it was found at. */
async function filesReachedFromTheReader(): Promise<string[]> {
  const bundled = await build({
    entryPoints: [new URL('./reader.ts', import.meta.url).pathname],
    bundle: true,
    write: false,
    metafile: true,
    logLevel: 'silent',
  });
  return Object.keys(bundled.metafile.inputs);
}

describe('the reader entry', () => {
  it('reaches the reader', async () => {
    expect((await filesReachedFromTheReader()).some((file) => file.endsWith('src/api/dashboardApi.ts'))).toBe(true);
  });

  it('reaches nothing of React, so a program with no browser can load it', async () => {
    const reached = await filesReachedFromTheReader();

    expect(reached.filter((file) => /node_modules\/(react|react-dom|react-i18next|scheduler)\//.test(file))).toEqual([]);
    expect(reached.filter((file) => file.endsWith('.tsx') || /\/hooks\//.test(file))).toEqual([]);
  });
});
