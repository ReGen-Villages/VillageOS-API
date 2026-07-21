export const BASE_LANGUAGE = 'en';

export const SUPPORTED_LANGUAGES = [
  { code: 'en', label: 'English', flag: '🇬🇧' },
  { code: 'de', label: 'Deutsch', flag: '🇩🇪' },
  { code: 'es', label: 'Español', flag: '🇪🇸' },
  { code: 'fr', label: 'Français', flag: '🇫🇷' },
  { code: 'it', label: 'Italiano', flag: '🇮🇹' },
  { code: 'nl', label: 'Nederlands', flag: '🇳🇱' },
] as const;

export type LanguageCode = (typeof SUPPORTED_LANGUAGES)[number]['code'];

const LANGUAGE_STORAGE_KEY = 'vos-language';

function isSupported(code: string): code is LanguageCode {
  return SUPPORTED_LANGUAGES.some((language) => language.code === code);
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
 * Only the primary subtag is considered (`es-MX` matches `es`).
 */
export function detectInitialLanguage(): LanguageCode {
  const stored = loadStoredLanguage();
  if (stored) return stored;

  const preferred = typeof navigator !== 'undefined' ? navigator.language : '';
  const primarySubtag = preferred.split('-')[0]?.toLowerCase() ?? '';
  if (isSupported(primarySubtag)) return primarySubtag;

  return BASE_LANGUAGE;
}
