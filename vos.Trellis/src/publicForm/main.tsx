import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { I18nextProvider, useTranslation } from 'react-i18next';
import { useEffect } from 'react';
import '../index.css';
import i18n from '../i18n';
import { directionFor } from '../i18n/languages';
import { useThemeStore, attachThemeMediaListener } from '../stores/themeStore';
import { PublicSubmissionPage } from './PublicSubmissionPage';

/** The signed-in application's shell does this for its own pages. This form is served on its own, so it
 *  mirrors the two things that shell sets on the document and nothing else it does. */
function PublicForm() {
  const theme = useThemeStore((state) => state.theme);
  const { i18n: active } = useTranslation();

  useEffect(() => {
    const root = document.documentElement;
    if (theme === 'dark') root.classList.add('dark');
    else root.classList.remove('dark');
  }, [theme]);

  useEffect(() => {
    const root = document.documentElement;
    root.setAttribute('lang', active.language);
    root.setAttribute('dir', directionFor(active.language));
  }, [active.language]);

  useEffect(() => attachThemeMediaListener(), []);

  return <PublicSubmissionPage />;
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nextProvider i18n={i18n}>
      <PublicForm />
    </I18nextProvider>
  </StrictMode>,
);
