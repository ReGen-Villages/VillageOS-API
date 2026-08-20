import { describe, it, expect } from 'vitest';
import { verdictSentence } from './verdictSentence';

describe('verdictSentence', () => {
  it('puts the judged figure and its target into the wording the model supplied', () => {
    const sentence = verdictSentence(
      '{value} of assumed consumption — short of the {target} target',
      { value: 73, target: 100 },
      'pct100',
    );

    expect(sentence).toBe('73% of assumed consumption — short of the 100% target');
  });

  it('formats the figure and the target the same way, so a sentence cannot mix units', () => {
    const sentence = verdictSentence('{value} against {target}', { value: 19.9, target: 14 }, 'decimal1', 'days');

    expect(sentence).toBe('19.9 days against 14.0 days');
  });

  // One wording serves a balance that was assessed and one that never was.
  it('drops a placeholder the verdict has no figure for, and the space beside it', () => {
    const sentence = verdictSentence('not assessed — no {value} was computed', { value: null, target: null });

    expect(sentence).toBe('not assessed — no was computed');
  });

  it('leaves wording that names no figure exactly as the model wrote it', () => {
    expect(verdictSentence('not assessed', { value: null, target: null })).toBe('not assessed');
  });

  // A missing figure must never read as a measurement of zero, and `formatNumber` would render one
  // as the dash it uses for an absent number if it were handed one.
  it('never substitutes a zero or a dash for a figure the verdict does not have', () => {
    expect(verdictSentence('{value} of consumption', { value: null, target: 100 }, 'pct100'))
      .toBe('of consumption');
  });

  it('shows a real zero as a zero', () => {
    expect(verdictSentence('{value} of consumption', { value: 0, target: 100 }, 'pct100')).toBe('0% of consumption');
  });
});
