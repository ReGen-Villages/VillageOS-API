import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import type { ParseKeys } from 'i18next';
import { directionFor } from '../i18n/languages';
import { attachThemeMediaListener, useThemeStore } from '../stores/themeStore';

/** A page served on its own has no application shell, so it sets the theme class, language, reading
 *  direction, tab title and light-or-dark listener itself. The title in each page's HTML shows only until
 *  the translations load. */
export function useStandalonePageDocument(title: ParseKeys): void {
  const { t, i18n } = useTranslation();
  const theme = useThemeStore((state) => state.theme);

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
  }, [theme]);

  useEffect(() => {
    const root = document.documentElement;
    root.setAttribute('lang', i18n.language);
    root.setAttribute('dir', directionFor(i18n.language));
    document.title = t(title);
  }, [i18n.language, t, title]);

  useEffect(() => attachThemeMediaListener(), []);
}
