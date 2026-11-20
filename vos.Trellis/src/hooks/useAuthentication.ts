import { createContext, useContext, useState, useCallback, useEffect, useRef } from 'react';
import { apiClient, type AuthenticatedUser } from '../api/client';
import { myceliumApi, type StartupProgress } from '../api/myceliumApi';
import { useModelStore } from '../stores/modelStore';
import type { ModelSummary } from '@tendril-ui/tendril';
import i18n from '../i18n';

interface AuthenticationState {
  isAuthenticated: boolean;
  user: AuthenticatedUser | null;
  role: string | null;
  modelId: string | null;
  modelName: string | null;
  availableModels: ModelSummary[] | null;
  mustChangePassword: boolean;
  startupProgress: StartupProgress | null;
  login: (username: string, password: string, modelId?: string) => Promise<void>;
  selectModel: (modelId: string) => Promise<void>;
  switchModel: () => Promise<void>;
  saveSeed: (name: string) => Promise<void>;
  changePassword: (currentPassword: string, newPassword: string) => Promise<void>;
  logout: () => Promise<void>;
  error: string | null;
  loading: boolean;
}

export const AuthenticationContext = createContext<AuthenticationState | null>(null);

function modelsFromSeeds(seeds: { name: string; sizeMb: number }[]): ModelSummary[] {
  return seeds.map((seed) => ({ Id: seed.name, Name: `${seed.name.replace('.seed.json', '')}  (${seed.sizeMb} MB)` }));
}

export function useAuthentication(): AuthenticationState {
  const context = useContext(AuthenticationContext);
  if (!context) throw new Error('useAuth must be used within AuthProvider');
  return context;
}

export function useAuthenticationState(): AuthenticationState {
  const [user, setUser] = useState<AuthenticatedUser | null>(apiClient.getUser());
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [modelId, setModelId] = useState<string | null>(apiClient.getModelId());
  const [modelName, setModelName] = useState<string | null>(apiClient.getModelName());
  const [availableModels, setAvailableModels] = useState<ModelSummary[] | null>(null);
  const [authenticationFailed, setAuthenticationFailed] = useState(false);
  const [startupProgress, setStartupProgress] = useState<StartupProgress | null>(null);
  const pollTimerReference = useRef<ReturnType<typeof setInterval> | null>(null);
  const pendingCredentialsReference = useRef<{ username: string; password: string } | null>(null);

  const isAuthenticated = !authenticationFailed && (apiClient.isAuthenticated() || !!import.meta.env.VITE_API_KEY);

  const stopPolling = useCallback(() => {
    if (pollTimerReference.current) {
      clearInterval(pollTimerReference.current);
      pollTimerReference.current = null;
    }
    pendingCredentialsReference.current = null;
  }, []);

  useEffect(() => stopPolling, [stopPolling]);

  const signedIn = useCallback((signedInUser: AuthenticatedUser | null) => {
    setUser(signedInUser);
    setModelId(apiClient.getModelId());
    setModelName(apiClient.getModelName());
    setAuthenticationFailed(false);
  }, []);

  useEffect(() => {
    apiClient.setAuthenticationRequiredCallback(() => {
      setUser(null);
      setModelId(null);
      setModelName(null);
      setAuthenticationFailed(true);
    });
    apiClient.setUserUpdatedCallback((updatedUser) => {
      setUser(updatedUser);
    });
  }, []);

  useEffect(() => {
    if (apiClient.isAuthenticated()) return;
    apiClient.restoreSession().then((restored) => {
      if (restored) signedIn(apiClient.getUser());
    }).catch(() => {});
  }, [signedIn]);

  useEffect(() => {
    if (import.meta.env.VITE_API_KEY && !apiClient.isAuthenticated()) {
      apiClient.ensureToken().catch(() => {});
    }
  }, []);

  const startSeedPolling = useCallback((username: string, password: string) => {
    pendingCredentialsReference.current = { username, password };
    if (pollTimerReference.current) return;

    const poll = async () => {
      try {
        const status = await myceliumApi.getStartupStatus();
        setStartupProgress(status);
        if (!status.IsLoading && status.Phase === 'Done') {
          // Read the credentials before stopPolling, which clears them.
          const credentials = pendingCredentialsReference.current;
          stopPolling();
          setStartupProgress(null);
          if (credentials) {
            try {
              signedIn(await apiClient.login(credentials.username, credentials.password));
              setAvailableModels(null);
              setError(null);
              setLoading(false);
            } catch {
              setError(i18n.t('authentication.seedLoadedSignInAgain'));
              setLoading(false);
            }
          }
        }
      } catch {
        // The broker may not be reachable yet.
      }
    };

    poll();
    pollTimerReference.current = setInterval(poll, 2000);
  }, [stopPolling, signedIn]);

  const login = useCallback(async (username: string, password: string, selectedModelId?: string) => {
    setLoading(true);
    setError(null);
    stopPolling();
    setStartupProgress(null);
    try {
      signedIn(await apiClient.login(username, password, selectedModelId));
      setAvailableModels(null);
    } catch (err: unknown) {
      // A multi-model login with no selection returns the available models in the error body.
      if (err instanceof Error && 'body' in err) {
        try {
          const parsed = JSON.parse((err as { body: string }).body);
          if (parsed.models) {
            setAvailableModels(parsed.models as ModelSummary[]);
            setError(null);
            return;
          }
        } catch { /* not a models response */ }
      }
      const message = err instanceof Error ? err.message : i18n.t('authentication.loginFailed');
      // "No models loaded" means either a seed still loading, which is waited for, or an empty library that
      // never finishes loading, which needs an error the person can act on; the startup status tells them apart.
      if (message.includes('No models loaded')) {
        try {
          const status = await myceliumApi.getStartupStatus();
          if (status.IsLoading) {
            setError(null);
            setStartupProgress(status);
            startSeedPolling(username, password);
            return;
          }
        } catch { /* the empty-library error below */ }
        setError(i18n.t('authentication.emptyLibrary'));
        throw err;
      }
      setError(message);
      throw err;
    } finally {
      setLoading(false);
    }
  }, [stopPolling, startSeedPolling, signedIn]);

  const logout = useCallback(async () => {
    useModelStore.getState().clear();
    setUser(null);
    setModelId(null);
    setModelName(null);
    setAvailableModels(null);
    setError(null);
    // apiClient.isAuthenticated(), which React does not watch, stays true until the call below returns, so
    // without this the next render still shows the person signed in.
    setAuthenticationFailed(true);
    await apiClient.logout();
  }, []);

  const switchModel = useCallback(async () => {
    setError(null);
    try {
      setAvailableModels(modelsFromSeeds(await myceliumApi.getLibrarySeeds()));
    } catch {
      setError(i18n.t('authentication.librarySeedsFailed'));
    }
  }, []);

  const changePassword = useCallback(async (currentPassword: string, newPassword: string) => {
    setLoading(true);
    setError(null);
    try {
      const userId = user?.Id;
      if (!userId) throw new Error('No user logged in');
      await apiClient.changePassword(userId, newPassword, currentPassword);
      setUser(previous => previous ? { ...previous, MustChangePassword: false } : null);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : i18n.t('changePassword.failed');
      setError(message);
      throw err;
    } finally {
      setLoading(false);
    }
  }, [user]);

  const selectModel = useCallback(async (seedName: string) => {
    setLoading(true);
    setError(null);
    try {
      const result = await myceliumApi.loadSeed(seedName);
      await apiClient.rescopeToModel(result.modelId);
      useModelStore.getState().clear();
      setModelId(result.modelId);
      setModelName(result.modelName);
      setAvailableModels(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : i18n.t('authentication.loadSeedFailed'));
    } finally {
      setLoading(false);
    }
  }, []);

  const saveSeed = useCallback(async (name: string) => {
    setLoading(true);
    setError(null);
    try {
      await myceliumApi.saveSeed(name);
      setAvailableModels(modelsFromSeeds(await myceliumApi.getLibrarySeeds()));
    } catch (err) {
      setError(err instanceof Error ? err.message : i18n.t('authentication.saveSeedFailed'));
    } finally {
      setLoading(false);
    }
  }, []);

  return {
    isAuthenticated: isAuthenticated || !!user,
    user,
    role: user?.Role ?? null,
    modelId,
    modelName,
    availableModels,
    mustChangePassword: user?.MustChangePassword ?? false,
    startupProgress,
    login,
    selectModel,
    switchModel,
    saveSeed,
    changePassword,
    logout,
    error,
    loading,
  };
}
