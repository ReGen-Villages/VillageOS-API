import { lazy, Suspense, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { directionFor } from './i18n/languages';
import { AppLayout } from './components/layout/AppLayout';
import { AuthenticationContext, useAuthenticationState, useAuthentication } from './hooks/useAuthentication';
import { LoginForm } from './components/auth/LoginForm';
import { ChangePasswordForm } from './components/auth/ChangePasswordForm';
import { useModelData } from './hooks/useModelData';
import { useUiStore } from './stores/uiStore';
import { loadPlatformPages } from './api/platformPages';
import { useThemeStore, attachThemeMediaListener } from './stores/themeStore';

const GraphPage = lazy(() => import('./pages/GraphPage').then(m => ({ default: m.GraphPage })));
const DashboardPage = lazy(() => import('./pages/DashboardPage').then(m => ({ default: m.DashboardPage })));
const OperationsPage = lazy(() => import('./pages/OperationsPage').then(m => ({ default: m.OperationsPage })));
const ComposerPage = lazy(() => import('./pages/ComposerPage').then(m => ({ default: m.ComposerPage })));
const DesignPage = lazy(() => import('./pages/DesignPage').then(m => ({ default: m.DesignPage })));
const IntakeWizardPage = lazy(() => import('./pages/IntakeWizardPage').then(m => ({ default: m.IntakeWizardPage })));
const SubmissionReviewPage = lazy(() => import('./pages/SubmissionReviewPage').then(m => ({ default: m.SubmissionReviewPage })));
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
  const { modelId, user } = useAuthentication();
  const setCurrentModelId = useUiStore((s) => s.setCurrentModelId);
  useEffect(() => {
    setCurrentModelId(modelId);
  }, [modelId, setCurrentModelId]);
  // What the platform declares depends on who signed in, not on which model is open.
  const accountId = user?.Id ?? null;
  useEffect(() => {
    if (accountId) void loadPlatformPages(accountId);
  }, [accountId]);

  return (
    <BrowserRouter>
      <Suspense fallback={null}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/operations" element={<OperationsPage />} />
            <Route path="/operations/:dashboardKey" element={<OperationsPage />} />
            <Route path="/compose" element={<ComposerPage />} />
            <Route path="/design" element={<DesignPage />} />
            <Route path="/design/:dashboardKey" element={<DesignPage />} />
            <Route path="/intake" element={<IntakeWizardPage />} />
            <Route path="/submissions" element={<SubmissionReviewPage />} />
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
  const authentication = useAuthenticationState();
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

  if (!authentication.isAuthenticated || authentication.availableModels) {
    return (
      <LoginForm
        onLogin={authentication.login}
        onSelectModel={authentication.selectModel}
        onSaveSeed={authentication.saveSeed}
        error={authentication.error}
        loading={authentication.loading}
        availableModels={authentication.availableModels}
        startupProgress={authentication.startupProgress}
      />
    );
  }

  if (authentication.mustChangePassword) {
    return (
      <ChangePasswordForm
        onChangePassword={authentication.changePassword}
        error={authentication.error}
        loading={authentication.loading}
        username={authentication.user?.Username ?? ''}
      />
    );
  }

  return (
    <AuthenticationContext.Provider value={authentication}>
      <AuthenticatedApp />
    </AuthenticationContext.Provider>
  );
}
