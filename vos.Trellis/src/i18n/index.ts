import i18next from 'i18next';
import { initReactI18next } from 'react-i18next';
import { en } from './locales/en';
import { de } from './locales/de';
import { es } from './locales/es';
import { fr } from './locales/fr';
import { it } from './locales/it';
import { nl } from './locales/nl';
import { BASE_LANGUAGE, detectInitialLanguage, persistLanguage } from './languages';
import type { LanguageCode } from './languages';

i18next.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    de: { translation: de },
    es: { translation: es },
    fr: { translation: fr },
    it: { translation: it },
    nl: { translation: nl },
  },
  lng: detectInitialLanguage(),
  fallbackLng: BASE_LANGUAGE,
  interpolation: {
    // React escapes rendered values, so i18next must not double-escape.
    escapeValue: false,
  },
});

/** Switch the active language and remember the choice for next visit. */
export function changeLanguage(code: LanguageCode): Promise<unknown> {
  persistLanguage(code);
  return i18next.changeLanguage(code);
}

export default i18next;
