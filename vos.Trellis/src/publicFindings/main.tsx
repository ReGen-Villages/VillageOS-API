import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { I18nextProvider } from 'react-i18next';
import '../index.css';
import i18n from '../i18n';
import { PublicFindingsPage } from './PublicFindingsPage';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nextProvider i18n={i18n}>
      <PublicFindingsPage />
    </I18nextProvider>
  </StrictMode>,
);
