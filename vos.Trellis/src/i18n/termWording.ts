import { useTranslation } from 'react-i18next';
import { BASE_LANGUAGE, primarySubtag } from './languages';

/** What each term a form offers is called in each language, as the model states it: the term's name,
 *  then a language code, then the words. The name is still what a submission sends. */
export type TermWording = Readonly<Record<string, Readonly<Record<string, string>>>>;

/** The words for a term in a language: the regional entry, then the language's, then English, then the
 *  term's own name — so a term the model words in no language still reads as the model names it. */
export function wordsFor(wording: TermWording, term: string, language: string): string {
  const words = wording[term];
  return words?.[language] ?? words?.[primarySubtag(language)] ?? words?.[BASE_LANGUAGE] ?? term;
}

/** Words by language read from a term's `wording` property, which holds them as JSON text. Anything
 *  that is not an object of words by language reads as none, and the term is shown by its name. */
export function wordingIn(stated: unknown): Record<string, string> | null {
  if (typeof stated !== 'string') return null;
  try {
    const parsed: unknown = JSON.parse(stated);
    if (parsed === null || typeof parsed !== 'object' || Array.isArray(parsed)) return null;
    const words = Object.entries(parsed).filter((entry): entry is [string, string] => typeof entry[1] === 'string');
    return words.length > 0 ? Object.fromEntries(words) : null;
  } catch {
    return null;
  }
}

/** A term's words in the reader's language, following a change of language. */
export function useTermWords(wording: TermWording): (term: string) => string {
  const { i18n } = useTranslation();
  return (term) => wordsFor(wording, term, i18n.language);
}
