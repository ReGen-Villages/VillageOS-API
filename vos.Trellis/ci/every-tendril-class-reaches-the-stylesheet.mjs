// Fails a build whose stylesheet lacks a class Tendril's widgets use.
//
// Tailwind writes a rule only for the classes it finds in the files it scans, and Tendril's files sit
// outside this folder, so a class used only there is dropped with nothing reported and the widget is
// drawn without it. It exists only in built output, so it is checked here, after the build.
//
//   node ci/every-tendril-class-reaches-the-stylesheet.mjs <built directory> [more directories]

import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { classNamesIn, missingFrom } from './tendrilClasses.mjs';

const directories = process.argv.slice(2);
if (directories.length === 0 || directories.some((directory) => !existsSync(directory))) {
  console.error('name the built directories to check, for instance dist and dist-public, after building them');
  process.exit(2);
}

const tendrilSource = fileURLToPath(new URL('../../vos.Tendril/src', import.meta.url));

function filesUnder(directory, keep) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) return filesUnder(path, keep);
    return keep(entry.name) ? [path] : [];
  });
}

const used = new Set(
  filesUnder(tendrilSource, (name) => /\.tsx?$/.test(name) && !name.includes('.test.')).flatMap((file) =>
    classNamesIn(readFileSync(file, 'utf8')),
  ),
);
let failed = false;
for (const built of directories) {
  const stylesheet = filesUnder(built, (name) => name.endsWith('.css'))
    .map((file) => readFileSync(file, 'utf8'))
    .join('\n');
  const missing = missingFrom(stylesheet, [...used]);
  if (missing.length > 0) {
    console.error(`The stylesheet built in ${built} has no rule for these classes Tendril uses: ${missing.sort().join(', ')}.`);
    failed = true;
  } else {
    console.log(`Every one of the ${used.size} classes Tendril uses is in the stylesheet built in ${built}.`);
  }
}
if (failed) {
  console.error('Tailwind did not scan Tendril\'s files; vos.Trellis/src/index.css names them with @source.');
  process.exit(1);
}
