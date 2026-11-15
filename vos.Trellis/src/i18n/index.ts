import i18next from 'i18next';
import { initReactI18next } from 'react-i18next';
import { en } from './locales/en';
import { de } from './locales/de';
import { es } from './locales/es';
import { fr } from './locales/fr';
import { it } from './locales/it';
import { nl } from './locales/nl';
import { ar } from './locales/ar';
import { BASE_LANGUAGE, TENDRIL_NAMESPACE, TENDRIL_WORDS } from '@tendril-ui/tendril';
import { detectInitialLanguage, persistLanguage } from './languages';
import type { LanguageCode } from './languages';

i18next.use(initReactI18next).init({
  resources: {
    en: { translation: en, [TENDRIL_NAMESPACE]: TENDRIL_WORDS.en },
    de: { translation: de, [TENDRIL_NAMESPACE]: TENDRIL_WORDS.de },
    es: { translation: es, [TENDRIL_NAMESPACE]: TENDRIL_WORDS.es },
    fr: { translation: fr, [TENDRIL_NAMESPACE]: TENDRIL_WORDS.fr },
    it: { translation: it, [TENDRIL_NAMESPACE]: TENDRIL_WORDS.it },
    nl: { translation: nl, [TENDRIL_NAMESPACE]: TENDRIL_WORDS.nl },
    // Both `ar-SA` and `ar-AE` resolve here via i18next's `ar-XX → ar` fallback.
    ar: { translation: ar, [TENDRIL_NAMESPACE]: TENDRIL_WORDS.ar },
  },
  lng: detectInitialLanguage(),
  fallbackLng: BASE_LANGUAGE,
  interpolation: {
    // React escapes rendered values, so i18next must not double-escape.
    escapeValue: false,
  },
});

export function changeLanguage(code: LanguageCode): Promise<unknown> {
  persistLanguage(code);
  return i18next.changeLanguage(code);
}

export default i18next;
