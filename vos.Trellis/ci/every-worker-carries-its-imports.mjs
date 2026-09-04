// Fails a build whose worker asks for a file the build did not produce.
//
// A worker named for its address alone is copied without its import graph, so the library's own
// second half is never emitted and the worker fails to load — no tile is ever parsed, no error is
// shown, and every map in every built deployment draws its background colour and nothing else
// (Bug #6910). It cannot be caught by a unit test: it exists only in built output, and the
// development server hides it by resolving the import out of the installed package.
//
//   node ci/every-worker-carries-its-imports.mjs <built directory> [more directories]

import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';

const directories = process.argv.slice(2);
if (directories.length === 0) {
  console.error('name the built directories to check, for instance dist and dist-public');
  process.exit(2);
}

/** What one file asks another file for. Read off `from` and `import(` rather than parsed, because the
 *  files are minified and what matters is only the specifier — which is written without a space after
 *  `import{…}`, so a pattern demanding one finds nothing at all.
 *
 *  Kept to specifiers that name a script file, so an ordinary string that happens to follow the word
 *  `from` is not mistaken for one, and addresses reaching another host are left alone. */
function scriptImportsIn(source) {
  const asked = new Set();
  for (const [, specifier] of source.matchAll(/(?:\bfrom|\bimport)\s*\(?\s*["']([^"']+)["']/g)) {
    if (/^[a-z]+:/i.test(specifier) || specifier.startsWith('//')) continue;
    if (!/\.(mjs|js)$/.test(specifier)) continue;
    asked.add(specifier);
  }
  return [...asked];
}

function filesUnder(directory) {
  const found = [];
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) found.push(...filesUnder(path));
    else if (/\.(mjs|js)$/.test(entry.name)) found.push(path);
  }
  return found;
}

let missing = 0;
for (const directory of directories) {
  if (!existsSync(directory)) {
    console.error(`${directory} does not exist — build before checking it.`);
    process.exit(2);
  }
  for (const file of filesUnder(directory)) {
    const source = readFileSync(file, 'utf8');
    for (const asked of scriptImportsIn(source)) {
      // A specifier naming a script resolves against the asking file, whether or not it says so with
      // a leading dot: that is how the browser reads the one the library's worker writes.
      const target = asked.startsWith('/')
        ? resolve(directory, `.${asked}`)
        : resolve(dirname(file), asked);
      if (existsSync(target)) continue;
      missing += 1;
      console.error(`${file} asks for ${asked}, which this build did not produce.`);
    }
  }
}

if (missing > 0) {
  console.error(
    `\n${missing} import(s) in the built output reach a file that is not there. A worker copied for `
    + 'its address alone is the usual cause: ask the bundler for it as a worker, so it is emitted '
    + 'whole.',
  );
  process.exit(1);
}
console.log(`Every relative import in ${directories.join(' and ')} reaches a file the build produced.`);
