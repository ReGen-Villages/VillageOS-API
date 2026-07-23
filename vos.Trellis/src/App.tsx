import { lazy, Suspense, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { directionFor } from './i18n/languages';
import { AppLayout } from './components/layout/AppLayout';
import { AuthContext, useAuthState, useAuth } from './hooks/useAuth';
import { LoginForm } from './components/auth/LoginForm';
import { ChangePasswordForm } from './components/auth/ChangePasswordForm';
import { useModelData } from './hooks/useModelData';
import { useUiStore } from './stores/uiStore';
import { useThemeStore, attachThemeMediaListener } from './stores/themeStore';

const GraphPage = lazy(() => import('./pages/GraphPage').then(m => ({ default: m.GraphPage })));
const DashboardPage = lazy(() => import('./pages/DashboardPage').then(m => ({ default: m.DashboardPage })));
const OperationsPage = lazy(() => import('./pages/OperationsPage').then(m => ({ default: m.OperationsPage })));
const TemporalPage = lazy(() => import('./pages/TemporalPage').then(m => ({ default: m.TemporalPage })));
const PropertySearchPage = lazy(() => import('./pages/PropertySearchPage').then(m => ({ default: m.PropertySearchPage })));
const ThingSearchPage = lazy(() => import('./pages/ThingSearchPage').then(m => ({ default: m.ThingSearchPage })));
const ModelPage = lazy(() => import('./pages/ModelPage').then(m => ({ default: m.ModelPage })));
const PipelinePage = lazy(() => import('./pages/PipelinePage').then(m => ({ default: m.PipelinePage })));
const LogPage = lazy(() => import('./pages/LogPage').then(m => ({ default: m.LogPage })));

/** Inner shell rendered only when authenticated. Owns the live model-data
 *  load so every page sees a populated store from mount. */
function AuthenticatedApp() {
  useModelData();
  // Per-model UI state (e.g. type filter) keys its localStorage entry off
  // the active modelId.
  const { modelId } = useAuth();
  const setCurrentModelId = useUiStore((s) => s.setCurrentModelId);
  useEffect(() => {
    setCurrentModelId(modelId);
  }, [modelId, setCurrentModelId]);

  return (
    <BrowserRouter>
      <Suspense fallback={null}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/operations" element={<OperationsPage />} />
            <Route path="/graph" element={<GraphPage />} />
            <Route path="/temporal" element={<TemporalPage />} />
            <Route path="/properties" element={<PropertySearchPage />} />
            <Route path="/things" element={<ThingSearchPage />} />
            <Route path="/model" element={<ModelPage />} />
            <Route path="/pipelines" element={<PipelinePage />} />
            <Route path="/logs" element={<LogPage />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      </Suspense>
    </BrowserRouter>
  );
}

export default function App() {
  const auth = useAuthState();
  const theme = useThemeStore((s) => s.theme);
  const { i18n } = useTranslation();

  // Mirror theme to <html class="dark"> so Tailwind's class-based dark variant flips.
  useEffect(() => {
    const root = document.documentElement;
    if (theme === 'dark') root.classList.add('dark');
    else root.classList.remove('dark');
  }, [theme]);

  // Mirror the active language to <html lang/dir> so Arabic lays out right-to-left.
  useEffect(() => {
    const root = document.documentElement;
    root.setAttribute('lang', i18n.language);
    root.setAttribute('dir', directionFor(i18n.language));
  }, [i18n.language]);

  useEffect(() => attachThemeMediaListener(), []);

  if (!auth.isAuthenticated || auth.availableModels) {
    return (
      <LoginForm
        onLogin={auth.login}
        onSelectModel={auth.selectModel}
        onSaveSeed={auth.saveSeed}
        error={auth.error}
        loading={auth.loading}
        availableModels={auth.availableModels}
        startupProgress={auth.startupProgress}
      />
    );
  }

  if (auth.mustChangePassword) {
    return (
      <ChangePasswordForm
        onChangePassword={auth.changePassword}
        error={auth.error}
        loading={auth.loading}
        username={auth.user?.Username ?? ''}
      />
    );
  }

  return (
    <AuthContext.Provider value={auth}>
      <AuthenticatedApp />
    </AuthContext.Provider>
  );
}
