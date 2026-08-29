import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { I18nextProvider } from 'react-i18next';
import '../index.css';
import i18n from '../i18n';
import { PublicSubmissionPage } from './PublicSubmissionPage';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nextProvider i18n={i18n}>
      <PublicSubmissionPage />
    </I18nextProvider>
  </StrictMode>,
);
