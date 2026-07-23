export const BASE_LANGUAGE = 'en';

export const SUPPORTED_LANGUAGES = [
  { code: 'en', label: 'English', flag: '🇬🇧' },
  { code: 'de', label: 'Deutsch', flag: '🇩🇪' },
  { code: 'es', label: 'Español', flag: '🇪🇸' },
  { code: 'fr', label: 'Français', flag: '🇫🇷' },
  { code: 'it', label: 'Italiano', flag: '🇮🇹' },
  { code: 'nl', label: 'Nederlands', flag: '🇳🇱' },
  { code: 'ar-SA', label: 'العربية (السعودية)', flag: '🇸🇦' },
  { code: 'ar-AE', label: 'العربية (الإمارات)', flag: '🇦🇪' },
] as const;

export type LanguageCode = (typeof SUPPORTED_LANGUAGES)[number]['code'];

const LANGUAGE_STORAGE_KEY = 'vos-language';

/** Primary subtag of a BCP 47 tag, lower-cased: `ar-SA` and `AR` both give `ar`. */
function primarySubtag(tag: string): string {
  return tag.split('-')[0]?.toLowerCase() ?? '';
}

function isSupported(code: string): code is LanguageCode {
  return SUPPORTED_LANGUAGES.some((language) => language.code === code);
}

/**
 * The supported language that best fits a browser/BCP 47 tag: an exact match on
 * the full tag wins (`ar-AE` → `ar-AE`), otherwise the first entry sharing the
 * primary subtag (`es-MX` → `es`, `ar-BH` → `ar-SA`), otherwise null.
 */
function matchSupportedLanguage(tag: string): LanguageCode | null {
  const normalized = tag.toLowerCase();
  const exact = SUPPORTED_LANGUAGES.find((language) => language.code.toLowerCase() === normalized);
  if (exact) return exact.code;

  const primary = primarySubtag(tag);
  const byPrimary = SUPPORTED_LANGUAGES.find((language) => primarySubtag(language.code) === primary);
  return byPrimary ? byPrimary.code : null;
}

const RIGHT_TO_LEFT_SUBTAGS = new Set(['ar']);

/** Whether a language reads right-to-left, keyed on its primary subtag. */
export function isRightToLeft(code: string): boolean {
  return RIGHT_TO_LEFT_SUBTAGS.has(primarySubtag(code));
}

/** The `dir` attribute value for a language code. */
export function directionFor(code: string): 'rtl' | 'ltr' {
  return isRightToLeft(code) ? 'rtl' : 'ltr';
}

export function loadStoredLanguage(): LanguageCode | null {
  if (typeof localStorage === 'undefined') return null;
  const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY);
  return stored && isSupported(stored) ? stored : null;
}

export function persistLanguage(code: LanguageCode): void {
  if (typeof localStorage === 'undefined') return;
  localStorage.setItem(LANGUAGE_STORAGE_KEY, code);
}

/**
 * The language to start in: an explicit stored choice wins, otherwise the
 * browser's preferred language if we support it, otherwise the base locale.
 */
export function detectInitialLanguage(): LanguageCode {
  const stored = loadStoredLanguage();
  if (stored) return stored;

  const preferred = typeof navigator !== 'undefined' ? navigator.language : '';
  return matchSupportedLanguage(preferred) ?? BASE_LANGUAGE;
}
