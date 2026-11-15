import '@testing-library/jest-dom/vitest';
import i18next from 'i18next';
import { initReactI18next } from 'react-i18next';
import { TENDRIL_NAMESPACE, TENDRIL_WORDS } from './i18n/words';

await i18next.use(initReactI18next).init({
  lng: 'en',
  resources: { en: { [TENDRIL_NAMESPACE]: TENDRIL_WORDS.en } },
  interpolation: { escapeValue: false },
});
