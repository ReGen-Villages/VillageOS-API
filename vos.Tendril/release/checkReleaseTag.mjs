import { readFileSync } from 'node:fs';
import { releaseRefusal } from './releaseTag.mjs';

const [reference, onDevelop] = process.argv.slice(2);
const { version } = JSON.parse(readFileSync(new URL('../package.json', import.meta.url), 'utf-8'));
const refusal = releaseRefusal({
  tag: reference.replace(/^refs\/tags\//, ''),
  version,
  onDevelop: onDevelop === 'true',
});

if (refusal) {
  console.error(refusal);
  process.exit(1);
}
console.log(`Publishing Tendril ${version}.`);
