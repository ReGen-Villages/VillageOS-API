import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@react-sigma/core/lib/style.css'
import './index.css'
import App from './App.tsx'
import { useUiStore } from './stores/uiStore'

// Exposed for the screenshot-capture script and dev tools.
if (import.meta.env.DEV) {
  (window as any).__vosStore = useUiStore;
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
