import { describe, it, expect } from 'vitest';
import type { Lever } from '../api/dashboardApi';
import type { VerdictRow } from '../types/dashboard';
import { figureText, leverSentence, originLines, verdictReadings, workingRuns } from './figureReadings';

describe('a figure as a card shows it', () => {
  it('writes a number in the widget’s format and the language asked for', () => {
    expect(figureText(1234.5, 'decimal1', 'de')).toBe('1.234,5');
  });

  it('shows a word the binding resolved to as it is', () => {
    expect(figureText('Running low', 'decimal1', 'en')).toBe('Running low');
  });

  it('writes a dash for nothing, for an empty word and for rows', () => {
    expect(figureText(null, undefined, 'en')).toBe('—');
    expect(figureText('', undefined, 'en')).toBe('—');
    expect(figureText([], undefined, 'en')).toBe('—');
  });
});

describe('where a figure came from', () => {
  it('reads one line per Thing, in the model’s wording', () => {
    expect(originLines([
      { origin: 'measured', reads: 'measured, resolved {resolvedAt} from {source}', source: 'Gauge 4', resolvedAt: '2 May' },
      { origin: 'assumed', reads: 'assumed from {source}', source: 'Reservoir' },
    ])).toEqual([
      { origin: 'measured', sentence: 'measured, resolved 2 May from Gauge 4' },
      { origin: 'assumed', sentence: 'assumed from Reservoir' },
    ]);
  });

  it('says nothing for a row that carries no origin, an origin left unworded, or wording that comes to nothing', () => {
    expect(originLines([
      { name: 'Spring 1' },
      { origin: 'sampled', reads: 'sampled' },
      { origin: 'stated' },
      { origin: 'stated', reads: '{source}' },
    ])).toEqual([]);
  });
});

describe('the verdicts a row reads as', () => {
  const row: VerdictRow = {
    label: 'Supply',
    format: 'decimal1',
    unit: 'days',
    verdicts: {
      kind: 'verdict',
      states: [
        { state: 'Short', reads: '{value} against a target of {target}', levers: { raise: 'more {term}', lower: 'less {term}' } },
        { state: 'Covered', reads: 'covered for {value}' },
      ],
    },
  };
  const stored: Lever = { term: 'storedLitres', direction: 'raise', words: { en: 'water stored', de: 'gespeichertes Wasser' } };
  const drawn: Lever = { term: 'drawnLitresPerDay', direction: 'lower' };
  const inflow: Lever = { term: 'flowLitresPerSecond', memberArchetype: 'Spring', direction: 'raise' };

  it('puts the figure and the target into the model’s wording, in the language asked for', () => {
    const [short] = verdictReadings(row, [{ state: 'Short', reads: '{value} against a target of {target}', value: 9.25, target: 14 }], 'de');

    expect(short).toMatchObject({ state: 'Short', sentence: '9,3 days against a target of 14,0 days' });
  });

  it('words each input a verdict offers with the wording of the direction it must move', () => {
    const [short] = verdictReadings(row, [{ state: 'Short', reads: 'short', value: 9, target: 14, levers: [stored, drawn, inflow] }], 'de');

    expect(short.levers.map(leverSentence)).toEqual([
      'more gespeichertes Wasser',
      'less drawnLitresPerDay',
      'more flowLitresPerSecond · Spring',
    ]);
    expect(short.levers[0]).toEqual({ lever: stored, before: 'more ', after: '', declaredName: 'gespeichertes Wasser' });
  });

  it('offers no inputs on a state the page description does not word them for', () => {
    const [covered] = verdictReadings(row, [{ state: 'Covered', reads: 'covered for {value}', value: 20, target: 14, levers: [stored] }], 'en');

    expect(covered).toEqual({ state: 'Covered', sentence: 'covered for 20.0 days', levers: [] });
  });

  it('reads wording that names no input as it stands', () => {
    const plain: VerdictRow = { ...row, verdicts: { kind: 'verdict', states: [{ state: 'Short', reads: 'short', levers: { raise: 'add storage', lower: 'draw less' } }] } };
    const [short] = verdictReadings(plain, [{ state: 'Short', reads: 'short', levers: [stored] }], 'en');

    expect(short.levers[0].after).toBeUndefined();
    expect(leverSentence(short.levers[0])).toBe('add storage');
  });

  it('reads nothing from a row bound to something other than a verdict', () => {
    const other: VerdictRow = { label: 'Supply', verdicts: { kind: 'thingList', archetype: 'Reservoir' } };

    expect(verdictReadings(other, [{ id: 'r1', name: 'Spring reservoir' }], 'en')).toEqual([]);
  });
});

describe('what a figure was worked out from', () => {
  it('keeps each formula with the inputs it produced, and writes each input plainly in the language asked for', () => {
    expect(workingRuns([
      { formula: 'storedLitres / drawnLitresPerDay', term: 'storedLitres', value: 12500.5 },
      { formula: 'storedLitres / drawnLitresPerDay', term: 'drawnLitresPerDay', value: 900 },
      { formula: 'a + b', term: 'a', value: null },
    ], 'de')).toEqual([
      { formula: 'storedLitres / drawnLitresPerDay', inputs: [{ term: 'storedLitres', value: '12.500,5' }, { term: 'drawnLitresPerDay', value: '900' }] },
      { formula: 'a + b', inputs: [{ term: 'a', value: '—' }] },
    ]);
  });

  it('names the kind of member that holds an input a total reads, in place of a value', () => {
    expect(workingRuns([
      { formula: null, term: 'flowLitresPerSecond', memberArchetype: 'Spring' },
      { formula: null, term: 'lossLitresPerSecond', memberArchetype: null },
    ], 'en')).toEqual([
      { formula: null, inputs: [{ term: 'flowLitresPerSecond', memberKind: 'Spring' }, { term: 'lossLitresPerSecond', memberKind: '' }] },
    ]);
  });

  it('reads no working as no runs', () => {
    expect(workingRuns([], 'en')).toEqual([]);
  });
});
