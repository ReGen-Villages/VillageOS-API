import { useTranslation } from 'react-i18next';
import { wordsFor, type TermWording } from '../i18n/termWording';

/** A term's words in the reader's language, following a change of language. */
export function useTermWords(wording: TermWording): (term: string) => string {
  const { i18n } = useTranslation();
  return (term) => wordsFor(wording, term, i18n.language);
}
