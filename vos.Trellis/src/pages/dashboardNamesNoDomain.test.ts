import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * The dashboard client ships widgets and a binding resolver; every word a reader sees comes from the
 * spec a model authors. That contract is what makes a second model cost nothing, and it decays one
 * widget at a time: each new widget written under pressure is a chance to spell a domain into the
 * client, and nothing else notices when one does.
 *
 * So this reads the source. A widget named for what one model measures — or a comment offering a
 * worked example in one model's words, which is what the next author copies — fails here.
 *
 * Silence a finding only by adding it to `ALLOWED` with the reason. A long list means the contract
 * is being argued with rather than kept.
 */
const SOURCE = join(dirname(fileURLToPath(import.meta.url)), '..');

/** Every file that draws or resolves this view. The widget directory is walked rather than listed,
 *  so a widget added later is judged without anyone remembering to add it here. */
const WIDGETS = join(SOURCE, 'components/dashboard/widgets');
const ALSO = [
  'pages/OperationsPage.tsx',
  'types/dashboard.ts',
  'api/dashboardApi.ts',
  'api/dashboardSubscription.ts',
  'api/dashboardLocalization.ts',
  'hooks/useDashboard.ts',
];

/** Nouns naming what a model is about rather than what a dashboard is made of. Matched from the
 *  start of a word, so plurals and endings are caught and a word merely containing one — rebuilding,
 *  website — is not. */
const DOMAIN = [
  'building', 'catchment', 'crop', 'dwelling', 'energy', 'farm', 'food', 'harvest', 'hectare',
  'irrigat', 'kwh', 'litre', 'liter', 'parcel', 'photovolta', 'planted', 'rainfall', 'reservoir',
  'site', 'solar', 'spring', 'village', 'water',
];

/** Findings that are legitimately not a domain word, each with the reason it stays. */
const ALLOWED = new Set<string>([]);

const DOMAIN_WORD = new RegExp(`\\b(${DOMAIN.join('|')})`, 'gi');

function domainWordsIn(text: string): string[] {
  return [...text.matchAll(DOMAIN_WORD)]
    .map((match) => match[0].toLowerCase())
    .filter((word) => !ALLOWED.has(word));
}

function sourceFilesUnder(directory: string): string[] {
  const out: string[] = [];
  for (const entry of readdirSync(directory)) {
    const full = join(directory, entry);
    if (statSync(full).isDirectory()) out.push(...sourceFilesUnder(full));
    else if (/\.tsx?$/.test(entry) && !entry.includes('.test.')) out.push(full);
  }
  return out;
}

const FILES = [...sourceFilesUnder(WIDGETS), ...ALSO.map((file) => join(SOURCE, file))];

describe('the dashboard client names no domain of its own', () => {
  it('reads the widgets it is meant to be reading, so an empty walk cannot pass this file', () => {
    expect(FILES.map((file) => relative(SOURCE, file))).toContain(
      'components/dashboard/widgets/WidgetRenderer.tsx',
    );
  });

  it('would report a domain word put in front of it', () => {
    expect(domainWordsIn('/** e.g. the water a site stores. */')).toEqual(['water', 'site']);
  });

  it('reports nothing for a word that merely contains one', () => {
    expect(domainWordsIn('rebuilding the index for this website')).toEqual([]);
  });

  for (const file of FILES) {
    it(`${relative(SOURCE, file)} holds none`, () => {
      expect(domainWordsIn(readFileSync(file, 'utf-8'))).toEqual([]);
    });
  }
});
