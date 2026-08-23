import { describe, it, expect } from 'vitest';
import { originSentence } from './originSentence';

const nothing = { source: null, resolvedAt: null };

describe('originSentence', () => {
  it('puts the source and the instant into the model\'s wording', () => {
    expect(originSentence('resolved {resolvedAt} from {source}', {
      source: 'Open rainfall archive',
      resolvedAt: '2026-08-19T09:12:00Z',
    })).toBe('resolved 2026-08-19T09:12:00Z from Open rainfall archive');
  });

  it('drops a placeholder with nothing to put in it, and the space beside it', () => {
    expect(originSentence('resolved {resolvedAt} from {source}', {
      source: 'Open rainfall archive',
      resolvedAt: null,
    })).toBe('resolved from Open rainfall archive');
  });

  it('reads as itself when the wording holds no placeholder', () => {
    expect(originSentence('as submitted', nothing)).toBe('as submitted');
  });

  // Never a dash in place of a source: a dash reads as a source nobody recorded, which is a
  // different claim from a figure that names none. What is left is the model's own words, which is
  // why the wording has to read with either placeholder gone.
  it('keeps only the wording when neither the source nor the instant is known', () => {
    expect(originSentence('resolved {resolvedAt} from {source}', nothing)).toBe('resolved from');
  });

  it('says nothing where the model gave no wording', () => {
    expect(originSentence('', { source: 'Open rainfall archive', resolvedAt: null })).toBe('');
  });
});
