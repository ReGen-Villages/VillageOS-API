import clsx from 'clsx';
import { useTranslation } from 'react-i18next';
import { changeLanguage } from '../../i18n';
import { SUPPORTED_LANGUAGES } from '../../i18n/languages';

/** A flag per language, wrapping to a compact grid so it fits the collapsed sidebar rail. */
export function LanguageSwitcher() {
  const { t, i18n } = useTranslation();

  return (
    <div role="group" aria-label={t('language.select')} className="flex flex-wrap justify-center gap-1">
      {SUPPORTED_LANGUAGES.map((language) => {
        const isActive = i18n.language === language.code;
        return (
          <button
            key={language.code}
            type="button"
            onClick={() => changeLanguage(language.code)}
            aria-label={language.label}
            aria-pressed={isActive}
            title={language.label}
            className={clsx(
              'flex h-5 w-6 items-center justify-center rounded text-base leading-none transition-opacity',
              isActive
                ? 'ring-1 ring-blue-500 bg-blue-100 dark:bg-blue-900/40'
                : 'opacity-50 hover:opacity-100',
            )}
          >
            <span aria-hidden="true">{language.flag}</span>
          </button>
        );
      })}
    </div>
  );
}
