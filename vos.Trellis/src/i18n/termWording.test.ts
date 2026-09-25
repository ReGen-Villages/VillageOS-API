import { describe, it, expect } from 'vitest';
import { wordingIn, wordsFor } from './termWording';

const wording = {
  'river-flood': { en: 'River flood', de: 'Flusshochwasser', ar: 'فيضان نهري', 'ar-AE': 'فيضان الأنهار' },
  high: { de: 'Hoch' },
};

describe('wordsFor', () => {
  it('reads the regional words first, then the language’s', () => {
    expect(wordsFor(wording, 'river-flood', 'ar-AE')).toBe('فيضان الأنهار');
    expect(wordsFor(wording, 'river-flood', 'ar-SA')).toBe('فيضان نهري');
    expect(wordsFor(wording, 'river-flood', 'de')).toBe('Flusshochwasser');
  });

  it('falls back to the English words for a language the term is not worded in', () => {
    expect(wordsFor(wording, 'river-flood', 'fr')).toBe('River flood');
  });

  it('falls back to the term’s own name where it is worded in neither', () => {
    expect(wordsFor(wording, 'high', 'fr')).toBe('high');
    expect(wordsFor(wording, 'no-data', 'de')).toBe('no-data');
  });
});

describe('wordingIn', () => {
  it('reads words by language out of the JSON text a term states', () => {
    expect(wordingIn('{"en": "High", "de": "Hoch"}')).toEqual({ en: 'High', de: 'Hoch' });
  });

  // Wording is only shown, so wording that cannot be read costs the words and nothing else.
  it.each([undefined, 3, 'High', '["High"]', '{"en": 3}', '{not json'])('reads %s as no wording', (stated) => {
    expect(wordingIn(stated)).toBeNull();
  });
});
