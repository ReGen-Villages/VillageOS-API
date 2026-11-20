// Fails a build whose worker asks for a file the build did not produce: that worker fails to load with no
// error, and every map draws only its background colour. Only built output shows it, so no unit test can.
//
//   node ci/every-worker-carries-its-imports.mjs <built directory> [more directories]

import { readdirSync, readFileSync, existsSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';

const directories = process.argv.slice(2);
if (directories.length === 0) {
  console.error('name the built directories to check, for instance dist and dist-public');
  process.exit(2);
}

/** Matched rather than parsed, because the files are minified and write `import{…}` with no space. Only a
 *  script file on this host counts, so a plain string after the word `from` is not taken for one. */
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
      // Resolved against the asking file even without a leading dot, as the browser resolves the one the library's worker writes.
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
