import 'i18next';
import type { TendrilWords } from '@villageos/tendril';
import type { Resources } from './locales/en';

declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'translation';
    resources: { translation: Resources; tendril: TendrilWords };
  }
}
