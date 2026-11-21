import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Every word a reader sees comes from the model's specification, which is what makes a second model cost
 * nothing, and each new widget is a chance to spell one model's domain into the client unnoticed. So this reads
 * the source, comments too, because a worked example in one model's words is what the next author copies. A
 * finding is silenced only in `ALLOWED`, with its reason.
 */
const SOURCE = join(dirname(fileURLToPath(import.meta.url)), '..');

/** Every file that draws or resolves this view. The widget directory is walked rather than listed,
 *  so a widget added later is judged without anyone remembering to add it here. */
const WIDGETS = join(SOURCE, 'components/dashboard/widgets');
const TENDRIL = join(SOURCE, '../../vos.Tendril/src');
const ALSO = [
  'pages/OperationsPage.tsx',
  'components/dashboard/DashboardSections.tsx',
  'publicPages/answeredFindings.ts',
  'api/dashboardSubscription.ts',
  'api/bindingVocabulary.ts',
  'api/dashboardLocalization.ts',
  'api/brokerModelReads.ts',
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

const FILES = [...sourceFilesUnder(WIDGETS), ...sourceFilesUnder(TENDRIL), ...ALSO.map((file) => join(SOURCE, file))];

describe('the dashboard client names no domain of its own', () => {
  it('reads the widgets it is meant to be reading, so an empty walk cannot pass this file', () => {
    expect(FILES.map((file) => relative(SOURCE, file))).toContain(
      'components/dashboard/widgets/WidgetRenderer.tsx',
    );
  });

  it('would report a domain word put in front of it', () => {
    expect(domainWordsIn('/** e.g. the water a site stores. */')).toEqual(['water', 'site']);
  });

  it('reads Tendril too, where the binding reader and the declarations live', () => {
    expect(FILES.map((file) => relative(SOURCE, file))).toContain('../../vos.Tendril/src/api/dashboardApi.ts');
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
