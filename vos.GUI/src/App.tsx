import { lazy, Suspense, useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AppLayout } from './components/layout/AppLayout';
import { AuthContext, useAuthState } from './hooks/useAuth';
import { LoginForm } from './components/auth/LoginForm';
import { ChangePasswordForm } from './components/auth/ChangePasswordForm';
import { useModelData } from './hooks/useModelData';
import { useThemeStore, attachThemeMediaListener } from './stores/themeStore';

const GraphPage = lazy(() => import('./pages/GraphPage').then(m => ({ default: m.GraphPage })));
const DashboardPage = lazy(() => import('./pages/DashboardPage').then(m => ({ default: m.DashboardPage })));
const TemporalPage = lazy(() => import('./pages/TemporalPage').then(m => ({ default: m.TemporalPage })));
const PropertySearchPage = lazy(() => import('./pages/PropertySearchPage').then(m => ({ default: m.PropertySearchPage })));
const ThingSearchPage = lazy(() => import('./pages/ThingSearchPage').then(m => ({ default: m.ThingSearchPage })));
const ModelPage = lazy(() => import('./pages/ModelPage').then(m => ({ default: m.ModelPage })));

/** Inner shell rendered only when authenticated. Owns the live model-data
 *  load so every page sees a populated store from mount (Feature #5329). */
function AuthenticatedApp() {
  useModelData();
  return (
    <BrowserRouter>
      <Suspense fallback={null}>
        <Routes>
          <Route element={<AppLayout />}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/graph" element={<GraphPage />} />
            <Route path="/temporal" element={<TemporalPage />} />
            <Route path="/properties" element={<PropertySearchPage />} />
            <Route path="/things" element={<ThingSearchPage />} />
            <Route path="/model" element={<ModelPage />} />
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

  // Mirror theme to <html class="dark"> so Tailwind's class-based dark variant
  // (configured in index.css) flips, and live-follow OS preference until the
  // user manually overrides via the toggle button.
  useEffect(() => {
    const root = document.documentElement;
    if (theme === 'dark') root.classList.add('dark');
    else root.classList.remove('dark');
  }, [theme]);

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
        seedStatus={auth.seedStatus}
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
