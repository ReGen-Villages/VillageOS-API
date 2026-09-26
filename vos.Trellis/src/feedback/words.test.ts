import { describe, it, expect } from 'vitest';
import { ENGLISH, WORDS_BY_LANGUAGE } from './locales/feedbackWords';
import { directionFor, fill, wordsFor } from './words';

describe('the words the report panel shows', () => {
  it('are the language asked for, matched by its first part', () => {
    expect(wordsFor('de-DE').send).toBe('Senden');
    expect(wordsFor('ar').send).toBe('إرسال');
  });

  it('fall back to English one word at a time', () => {
    WORDS_BY_LANGUAGE.xx = { send: 'Xend' };
    try {
      const words = wordsFor('xx');
      expect(words.send).toBe('Xend');
      expect(words.cancel).toBe(ENGLISH.cancel);
    } finally {
      delete WORDS_BY_LANGUAGE.xx;
    }
  });

  it('are English for a language the panel does not hold, or none at all', () => {
    expect(wordsFor('ja').send).toBe(ENGLISH.send);
    expect(wordsFor(undefined).send).toBe(ENGLISH.send);
  });

  it('lay out right to left for Arabic only', () => {
    expect(directionFor('ar-SA')).toBe('rtl');
    expect(directionFor('nl')).toBe('ltr');
  });

  it('fill a named value into its place', () => {
    expect(fill('Try again in {seconds} seconds.', { seconds: 40 })).toBe('Try again in 40 seconds.');
    expect(fill('Filed as {reference}.', {})).toBe('Filed as {reference}.');
  });

  it('are held for every word in every language', () => {
    for (const [language, words] of Object.entries(WORDS_BY_LANGUAGE)) {
      expect(Object.keys(words).sort(), language).toEqual(Object.keys(ENGLISH).sort());
    }
  });
});
