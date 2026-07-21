import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { I18nextProvider } from 'react-i18next'
import '@react-sigma/core/lib/style.css'
import './index.css'
import App from './App.tsx'
import i18n from './i18n'
import { useUiStore } from './stores/uiStore'

// Exposed for the screenshot-capture script and dev tools.
if (import.meta.env.DEV) {
  (window as Window & { __vosStore?: typeof useUiStore }).__vosStore = useUiStore;
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <I18nextProvider i18n={i18n}>
      <App />
    </I18nextProvider>
  </StrictMode>,
)
