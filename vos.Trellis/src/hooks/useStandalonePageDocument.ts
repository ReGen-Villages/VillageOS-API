import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { directionFor } from '../i18n/languages';
import { attachThemeMediaListener, useThemeStore } from '../stores/themeStore';

/**
 * Sets up the document for a page served on its own rather than inside the signed-in application:
 * the theme class, the language and reading direction, and the listener that follows the operating
 * system's light-or-dark setting. The application shell does all three for its own pages, and a
 * standalone page has no shell — so it does this, and nothing else the shell does.
 */
export function useStandalonePageDocument(): void {
  const { i18n } = useTranslation();
  const theme = useThemeStore((state) => state.theme);

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
  }, [theme]);

  useEffect(() => {
    const root = document.documentElement;
    root.setAttribute('lang', i18n.language);
    root.setAttribute('dir', directionFor(i18n.language));
  }, [i18n.language]);

  useEffect(() => attachThemeMediaListener(), []);
}
