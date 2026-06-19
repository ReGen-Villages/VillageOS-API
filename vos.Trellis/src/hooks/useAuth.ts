import { createContext, useContext, useState, useCallback, useEffect, useRef } from 'react';
import { apiClient, type AuthUser } from '../api/client';
import { myceliumApi, type StartupProgress } from '../api/myceliumApi';
import { useModelStore } from '../stores/modelStore';
import type { ModelSummary } from '../types/vos';

export interface AuthState {
  isAuthenticated: boolean;
  user: AuthUser | null;
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

export const AuthContext = createContext<AuthState | null>(null);

export function useAuth(): AuthState {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}

export function useAuthState(): AuthState {
  const [user, setUser] = useState<AuthUser | null>(apiClient.getUser());
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [modelId, setModelId] = useState<string | null>(apiClient.getModelId());
  const [modelName, setModelName] = useState<string | null>(apiClient.getModelName());
  const [availableModels, setAvailableModels] = useState<ModelSummary[] | null>(null);
  const [authFailed, setAuthFailed] = useState(false);
  const [startupProgress, setStartupProgress] = useState<StartupProgress | null>(null);
  const pollTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const pendingCredsRef = useRef<{ username: string; password: string } | null>(null);

  const isAuthenticated = !authFailed && (apiClient.isAuthenticated() || !!import.meta.env.VITE_API_KEY);

  const stopPolling = useCallback(() => {
    if (pollTimerRef.current) {
      clearInterval(pollTimerRef.current);
      pollTimerRef.current = null;
    }
    pendingCredsRef.current = null;
  }, []);

  useEffect(() => stopPolling, [stopPolling]);

  useEffect(() => {
    apiClient.setAuthRequiredCallback(() => {
      setUser(null);
      setModelId(null);
      setModelName(null);
      setAuthFailed(true);
    });
    apiClient.setUserUpdatedCallback((updatedUser) => {
      setUser(updatedUser);
    });
  }, []);

  // Restore a prior session from the HttpOnly cookie if its JWT is still valid.
  useEffect(() => {
    if (apiClient.isAuthenticated()) return;
    apiClient.restoreSession().then((restored) => {
      if (restored) {
        setUser(apiClient.getUser());
        setModelId(apiClient.getModelId());
        setModelName(apiClient.getModelName());
        setAuthFailed(false);
      }
    }).catch(() => {});
  }, []);

  useEffect(() => {
    if (import.meta.env.VITE_API_KEY && !apiClient.isAuthenticated()) {
      apiClient.ensureToken().catch(() => {});
    }
  }, []);

  const startSeedPolling = useCallback((username: string, password: string) => {
    pendingCredsRef.current = { username, password };
    if (pollTimerRef.current) return;

    const poll = async () => {
      try {
        const status = await myceliumApi.getStartupStatus();
        setStartupProgress(status);
        if (!status.IsLoading && status.Phase === 'Done') {
          // Read creds BEFORE stopPolling (which clears pendingCredsRef)
          const creds = pendingCredsRef.current;
          stopPolling();
          setStartupProgress(null);
          if (creds) {
            try {
              const u = await apiClient.login(creds.username, creds.password);
              setUser(u);
              setModelId(apiClient.getModelId());
              setModelName(apiClient.getModelName());
              setAvailableModels(null);
              setAuthFailed(false);
              setError(null);
              setLoading(false);
            } catch {
              setError('Seed loaded. Please sign in again.');
              setLoading(false);
            }
          }
        }
      } catch {
        // Mycelium might not be reachable yet — keep polling
      }
    };

    poll();
    pollTimerRef.current = setInterval(poll, 2000);
  }, [stopPolling]);

  const login = useCallback(async (username: string, password: string, selectedModelId?: string) => {
    setLoading(true);
    setError(null);
    stopPolling();
    setStartupProgress(null);
    try {
      const u = await apiClient.login(username, password, selectedModelId);
      setUser(u);
      setModelId(apiClient.getModelId());
      setModelName(apiClient.getModelName());
      setAvailableModels(null);
      setAuthFailed(false);
    } catch (err: unknown) {
      // A multi-model login with no selection returns the available models in the error body.
      if (err instanceof Error && 'body' in err) {
        try {
          const parsed = JSON.parse((err as { body: string }).body);
          if (parsed.models) {
            setAvailableModels(parsed.models as ModelSummary[]);
            setError('Please select a model');
            return;
          }
        } catch { /* not a models response */ }
      }
      const msg = err instanceof Error ? err.message : 'Login failed';
      // "No models loaded" is ambiguous: a seed loading at startup (poll + retry)
      // vs. an empty library that will never reach Phase=Done (Bug #5324 — surface
      // an actionable error). Disambiguate via SeedLoadingStatus before deciding.
      if (msg.includes('No models loaded')) {
        try {
          const status = await myceliumApi.getStartupStatus();
          if (status.IsLoading) {
            setError(null);
            setStartupProgress(status);
            startSeedPolling(username, password);
            return;
          }
        } catch { /* fall through to actionable error */ }
        setError(
          'No models on Mycelium. Drop a .seed.json into vos.Mycelium/seeds-library/ and reload, '
          + 'or POST /api/mycelium/library-seeds/<name>/load.',
        );
        throw err;
      }
      setError(msg);
      throw err;
    } finally {
      setLoading(false);
    }
  }, [stopPolling, startSeedPolling]);

  const logout = useCallback(async () => {
    // setAuthFailed(true) is load-bearing (Bug #5325): it forces the next render
    // to see isAuthenticated as false even though apiClient.isAuthenticated()
    // (a non-React field) stays true until the awaited round-trip completes.
    // login() resets authFailed on success.
    useModelStore.getState().clear();
    setUser(null);
    setModelId(null);
    setModelName(null);
    setAvailableModels(null);
    setError(null);
    setAuthFailed(true);
    await apiClient.logout();
  }, []);

  const switchModel = useCallback(async () => {
    setError(null);
    try {
      const seeds = await myceliumApi.getLibrarySeeds();
      const asModels: ModelSummary[] = seeds.map((s) => ({
        Id: s.name,
        Name: `${s.name.replace('.seed.json', '')}  (${s.sizeMb} MB)`,
      }));
      setAvailableModels(asModels);
    } catch {
      setError('Failed to fetch library seeds');
    }
  }, []);

  const changePassword = useCallback(async (currentPassword: string, newPassword: string) => {
    setLoading(true);
    setError(null);
    try {
      const userId = user?.Id;
      if (!userId) throw new Error('No user logged in');
      await apiClient.changePassword(userId, newPassword, currentPassword);
      setUser(prev => prev ? { ...prev, MustChangePassword: false } : null);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Password change failed';
      setError(msg);
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
      // Re-scope the JWT to the newly loaded model (works for login and API-key auth).
      await apiClient.rescopeToModel(result.modelId);
      useModelStore.getState().clear();
      setModelId(result.modelId);
      setModelName(result.modelName);
      setAvailableModels(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load seed');
    } finally {
      setLoading(false);
    }
  }, []);

  const saveSeed = useCallback(async (name: string) => {
    setLoading(true);
    setError(null);
    try {
      await myceliumApi.saveSeed(name);
      const seeds = await myceliumApi.getLibrarySeeds();
      const asModels: ModelSummary[] = seeds.map((s) => ({
        Id: s.name,
        Name: `${s.name.replace('.seed.json', '')}  (${s.sizeMb} MB)`,
      }));
      setAvailableModels(asModels);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save seed');
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
