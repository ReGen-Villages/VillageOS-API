import { Languages } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { changeLanguage } from '../../i18n';
import { SUPPORTED_LANGUAGES } from '../../i18n/languages';
import type { LanguageCode } from '../../i18n/languages';

interface LanguageSwitcherProps {
  /** Hide the leading icon when the surrounding chrome is collapsed. */
  showIcon?: boolean;
}

export function LanguageSwitcher({ showIcon = true }: LanguageSwitcherProps) {
  const { t, i18n } = useTranslation();

  return (
    <label className="flex items-center gap-2 text-zinc-500 dark:text-zinc-400">
      {showIcon && <Languages size={16} aria-hidden="true" />}
      <span className="sr-only">{t('language.label')}</span>
      <select
        value={i18n.language}
        onChange={(event) => changeLanguage(event.target.value as LanguageCode)}
        aria-label={t('language.select')}
        data-testid="language-switcher"
        className="flex-1 bg-transparent text-sm rounded border border-zinc-300 dark:border-zinc-700 px-2 py-1 hover:border-zinc-400 dark:hover:border-zinc-600 focus:outline-none focus:ring-1 focus:ring-blue-500 transition-colors"
      >
        {SUPPORTED_LANGUAGES.map((language) => (
          <option key={language.code} value={language.code}>
            {language.label}
          </option>
        ))}
      </select>
    </label>
  );
}
