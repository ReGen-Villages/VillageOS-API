import { lazy, Suspense } from 'react';
import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AppLayout } from './components/layout/AppLayout';
import { AuthContext, useAuthState } from './hooks/useAuth';
import { useModelLoader } from './hooks/useModelLoader';
import { LoginForm } from './components/auth/LoginForm';
import { ChangePasswordForm } from './components/auth/ChangePasswordForm';

const GraphPage = lazy(() => import('./pages/GraphPage').then(m => ({ default: m.GraphPage })));
const DashboardPage = lazy(() => import('./pages/DashboardPage').then(m => ({ default: m.DashboardPage })));
const TemporalPage = lazy(() => import('./pages/TemporalPage').then(m => ({ default: m.TemporalPage })));
const PropertySearchPage = lazy(() => import('./pages/PropertySearchPage').then(m => ({ default: m.PropertySearchPage })));
const ThingSearchPage = lazy(() => import('./pages/ThingSearchPage').then(m => ({ default: m.ThingSearchPage })));

/** Inner shell rendered only when authenticated — kicks off phased model loading. */
function AuthenticatedApp() {
  useModelLoader();

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
            <Route path="*" element={<Navigate to="/" replace />} />
          </Route>
        </Routes>
      </Suspense>
    </BrowserRouter>
  );
}

export default function App() {
  const auth = useAuthState();

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
