import { ENGLISH, RIGHT_TO_LEFT_LANGUAGES, WORDS_BY_LANGUAGE, type FeedbackWords } from './locales/feedbackWords';

function primaryPart(language: string | undefined): string {
  return (language ?? '').toLowerCase().split(/[-_]/)[0];
}

export function wordsFor(language: string | undefined): FeedbackWords {
  return { ...ENGLISH, ...WORDS_BY_LANGUAGE[primaryPart(language)] };
}

export function directionFor(language: string | undefined): 'rtl' | 'ltr' {
  return RIGHT_TO_LEFT_LANGUAGES.has(primaryPart(language)) ? 'rtl' : 'ltr';
}

/** A named value put in its `{name}` place. A place with no value is left showing, which a reader
 *  can still make sense of where a blank would read as a missing word. */
export function fill(words: string, values: Record<string, unknown>): string {
  return words.replace(/\{(\w+)\}/g, (place, name: string) => (name in values ? String(values[name]) : place));
}
