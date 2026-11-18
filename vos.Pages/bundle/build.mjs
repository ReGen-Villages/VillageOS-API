import { createRequire } from 'node:module';
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const tendril = path.resolve(here, '../../vos.Tendril');
const fromTendril = createRequire(path.join(tendril, 'package.json'));
const esbuild = fromTendril('esbuild');
const bundlePath = path.resolve(here, '../reader.bundle.js');

const built = await esbuild.build({
  entryPoints: [path.join(here, 'readerHost.ts')],
  bundle: true,
  write: false,
  format: 'iife',
  target: 'es2022',
  charset: 'utf8',
  legalComments: 'none',
  absWorkingDir: path.resolve(here, '../..'),
  // A worktree links node_modules to another checkout's, and a followed link would write that
  // checkout's path into the bundle, so the same source would build to different bytes there.
  preserveSymlinks: true,
  nodePaths: [path.join(tendril, 'node_modules')],
});
const bundle = built.outputFiles[0].text;

if (process.argv.includes('--check')) {
  if (readFileSync(bundlePath, 'utf8') !== bundle) {
    console.error('vos.Pages/reader.bundle.js is not what the reader builds to. Run: node vos.Pages/bundle/build.mjs');
    process.exit(1);
  }
} else {
  writeFileSync(bundlePath, bundle);
}
