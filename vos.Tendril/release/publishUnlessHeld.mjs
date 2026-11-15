import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { feedHolds } from './feedAnswer.mjs';

const [userConfiguration] = process.argv.slice(2);
const packageFolder = fileURLToPath(new URL('..', import.meta.url));
const { name, version } = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf-8'));

const view = spawnSync('npm', ['view', `${name}@${version}`, 'version', '--json', '--userconfig', userConfiguration], {
  cwd: packageFolder,
  encoding: 'utf-8',
});

let held;
try {
  held = feedHolds(view, version);
} catch (error) {
  console.error(error.message);
  process.exit(1);
}

if (held) {
  console.log(`The feed already holds Tendril ${version}.`);
  process.exit(0);
}

const publish = spawnSync('npm', ['publish', '--userconfig', userConfiguration], { cwd: packageFolder, stdio: 'inherit' });
process.exit(publish.status ?? 1);
