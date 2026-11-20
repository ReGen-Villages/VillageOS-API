/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { BINDING_FIELDS, unimplementedWordsIn } from './bindingVocabulary';
import type { Widget } from '@tendril-ui/tendril';

const VOCABULARY = join(dirname(fileURLToPath(import.meta.url)), '../../../vos.Tendril/src/types/dashboard.ts');

function tileShowing(value: unknown): Widget {
  return { type: 'kpi', title: 'a figure', value } as Widget;
}

describe('what a widget asks for that this build cannot answer', () => {
  it('says nothing about a widget whose every binding is one it resolves', () => {
    expect(unimplementedWordsIn(tileShowing({ kind: 'property', thing: '$scope', property: 'volume' })))
      .toEqual([]);
  });

  it('names a binding kind the vocabulary does not declare', () => {
    expect(unimplementedWordsIn(tileShowing({ kind: 'runningTotal', property: 'volume' })))
      .toEqual(['runningTotal']);
  });

  it('names only the kind when the kind itself is unknown', () => {
    const words = unimplementedWordsIn(tileShowing({ kind: 'runningTotal', over: 'a week', of: 'volume' }));
    expect(words).toEqual(['runningTotal']);
  });

  it('reports no word at all for a binding naming no kind', () => {
    expect(unimplementedWordsIn(tileShowing({ property: 'volume' }))).toEqual(['']);
  });

  it('names a field the kind it belongs to does not read', () => {
    expect(unimplementedWordsIn(tileShowing({
      kind: 'timeseries', archetype: 'Reading', happenedAt: 'recordedAt', reduction: 'sum',
      property: 'volume', bucketSeconds: 900, buckets: 32, smoothing: 'exponential',
    }))).toEqual(['smoothing']);
  });

  it('says nothing about a field the kind does read', () => {
    expect(unimplementedWordsIn(tileShowing({
      kind: 'timeseries', archetype: 'Reading', happenedAt: 'recordedAt', reduction: 'sum',
      property: 'volume', bucketSeconds: 900, buckets: 32, bucketsPerPoint: 4,
    }))).toEqual([]);
  });

  it('judges the halves of a ratio', () => {
    expect(unimplementedWordsIn(tileShowing({
      kind: 'ratio',
      numerator: { kind: 'aggregate', archetype: 'Reading', reduction: 'sum', property: 'volume' },
      denominator: { kind: 'runningTotal', property: 'volume' },
    }))).toEqual(['runningTotal']);
  });

  it('judges the series a tile reads its newest point from', () => {
    expect(unimplementedWordsIn(tileShowing({
      kind: 'latest',
      series: {
        kind: 'timeseries', archetype: 'Reading', happenedAt: 'recordedAt', reduction: 'sum',
        property: 'volume', bucketSeconds: 900, buckets: 32, smoothing: 'exponential',
      },
    }))).toEqual(['smoothing']);
  });

  it('names each word once however many bindings ask for it', () => {
    expect(unimplementedWordsIn(tileShowing({
      kind: 'ratio',
      numerator: { kind: 'runningTotal', property: 'volume' },
      denominator: { kind: 'runningTotal', property: 'area' },
    }))).toEqual(['runningTotal']);
  });

  // This runs on the way to drawing, so throwing here would take down the whole view, not the one wrong widget.
  it('survives a binding whose nested series was never written', () => {
    expect(() => unimplementedWordsIn(tileShowing({ kind: 'latest' }))).not.toThrow();
  });

  it('survives a ratio whose halves were never written', () => {
    expect(() => unimplementedWordsIn(tileShowing({ kind: 'ratio' }))).not.toThrow();
  });

  it('survives a computed column that holds no binding', () => {
    expect(() => unimplementedWordsIn(tileShowing({
      kind: 'thingList', archetype: 'Reading', computed: [{ key: 'reach' }],
    }))).not.toThrow();
  });

  /** A word this module made up would reach a reader untranslated, inside a translated sentence. Any branch
   *  could do it, so this is checked over many malformed specifications rather than one. The empty string
   *  is inside every text, so it passes here and the tests above judge it. */
  it('never reports a word the specification did not contain', () => {
    const malformed: unknown[] = [
      { kind: 'runningTotal', property: 'volume' },
      { kind: '', property: 'volume' },
      { property: 'volume' },
      { kind: 'latest' },
      { kind: 'ratio' },
      { kind: 'timeseries', archetype: 'Reading', smoothing: 'exponential' },
      { kind: 'latest', series: { kind: 'sankey' } },
      {},
    ];

    for (const value of malformed) {
      const widget = tileShowing(value);
      const specification = JSON.stringify(widget);
      for (const word of unimplementedWordsIn(widget)) {
        expect(specification, `"${word}" is not in the specification it was reported for`).toContain(word);
      }
    }
  });

  it('judges every binding slot a widget declares, not only its first', () => {
    const tile = {
      type: 'kpi', title: 'a figure',
      value: { kind: 'property', thing: '$scope', property: 'volume' },
      spark: { kind: 'runningTotal', property: 'volume' },
    } as unknown as Widget;
    expect(unimplementedWordsIn(tile)).toEqual(['runningTotal']);
  });
});

/** The compiler refuses a kind missing from the table and a field its kind lacks, but not an entry missing a
 *  field, so this reads the vocabulary as source. Braces rather than lines, because arms are written both on
 *  one line and over many; the table cannot lack a kind, so a parse finding fewer arms has misread the file. */
describe('the table of fields each kind reads', () => {
  const DECLARATION = /^\s*(\w+)\??\s*:/;
  const KIND_LITERAL = /^\s*kind\s*:\s*'([^']+)'/;

  function withoutComments(source: string): string {
    return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/\/\/[^\n]*/g, '');
  }

  /** Split on the separators that sit at the top level of the text, ignoring any nested in braces. */
  function topLevelParts(text: string, separator: string): string[] {
    const parts: string[] = [];
    let depth = 0;
    let start = 0;
    for (let at = 0; at < text.length; at++) {
      const character = text[at];
      if (character === '{') depth++;
      else if (character === '}') depth--;
      else if (character === separator && depth === 0) {
        parts.push(text.slice(start, at));
        start = at + 1;
      }
    }
    parts.push(text.slice(start));
    return parts;
  }

  function armsOf(source: string): string[] {
    const opening = source.indexOf('export type Binding =');
    if (opening < 0) return [];
    const text = withoutComments(source.slice(opening + 'export type Binding ='.length));
    const arms: string[] = [];
    let depth = 0;
    let armStart = -1;
    for (let at = 0; at < text.length; at++) {
      const character = text[at];
      if (character === '{') {
        if (depth === 0) armStart = at + 1;
        depth++;
      } else if (character === '}') {
        depth--;
        if (depth === 0) arms.push(text.slice(armStart, at));
      } else if (character === ';' && depth === 0) {
        break;
      }
    }
    return arms;
  }

  function readArm(arm: string): { kind: string | undefined; fields: string[] } {
    const parts = topLevelParts(arm, ';').filter((part) => part.trim() !== '');
    let kind: string | undefined;
    const fields: string[] = [];
    for (const part of parts) {
      const literal = part.match(KIND_LITERAL);
      if (literal) {
        kind = literal[1];
        continue;
      }
      const declared = part.match(DECLARATION);
      if (declared) fields.push(declared[1]);
    }
    return { kind, fields };
  }

  const source = readFileSync(VOCABULARY, 'utf8');
  const arms = armsOf(source).map(readArm);
  const tabled = Object.keys(BINDING_FIELDS).sort();

  // A parse that found nothing agrees with everything.
  it('reads the vocabulary at all', () => {
    expect(arms.length, `no arms of the Binding union were read from ${VOCABULARY}`).toBeGreaterThan(0);
  });

  it('finds as many arms as the table has kinds', () => {
    expect(arms.length, 'the union was read as fewer arms than the table declares kinds').toBe(tabled.length);
  });

  it('reads a kind from every arm it found', () => {
    expect(arms.filter((arm) => !arm.kind), 'an arm was read whose kind could not be made out').toEqual([]);
  });

  it('knows every kind the vocabulary declares, and no others', () => {
    expect(arms.map((arm) => arm.kind!).sort()).toEqual(tabled);
  });

  it('names every field its kind declares', () => {
    const short = arms
      .map((arm) => ({
        kind: arm.kind!,
        missing: arm.fields.filter(
          (field) => !(BINDING_FIELDS[arm.kind! as keyof typeof BINDING_FIELDS] as readonly string[]).includes(field),
        ),
      }))
      .filter((entry) => entry.missing.length > 0);

    expect(short, 'these fields are declared on a binding and left out of the table, so a specification setting one is silently tolerated')
      .toEqual([]);
  });
});
