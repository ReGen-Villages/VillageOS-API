import { createContext, useContext, useState, useCallback, useEffect, useRef } from 'react';
import { apiClient, type AuthUser } from '../api/client';
import { brokerApi, type SeedStatus } from '../api/brokerApi';
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
  seedStatus: SeedStatus | null;
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

/**
 * Hook that provides auth state management. Used by the AuthProvider component.
 * Tries VITE_API_KEY auto-login on mount if available.
 */
export function useAuthState(): AuthState {
  const [user, setUser] = useState<AuthUser | null>(apiClient.getUser());
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [modelId, setModelId] = useState<string | null>(apiClient.getModelId());
  const [modelName, setModelName] = useState<string | null>(apiClient.getModelName());
  const [availableModels, setAvailableModels] = useState<ModelSummary[] | null>(null);
  const [authFailed, setAuthFailed] = useState(false);
  const [seedStatus, setSeedStatus] = useState<SeedStatus | null>(null);
  const pollTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const pendingCredsRef = useRef<{ username: string; password: string } | null>(null);

  const isAuthenticated = !authFailed && (apiClient.isAuthenticated() || !!import.meta.env.VITE_API_KEY);

  // Stop polling when component unmounts or auth succeeds
  const stopPolling = useCallback(() => {
    if (pollTimerRef.current) {
      clearInterval(pollTimerRef.current);
      pollTimerRef.current = null;
    }
    pendingCredsRef.current = null;
  }, []);

  useEffect(() => stopPolling, [stopPolling]);

  // Register callbacks so the client can trigger re-render on auth changes
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

  // On mount, attempt to restore a prior session from the HttpOnly cookie.
  // If the cookie exists and the JWT inside is still valid, the broker returns
  // the token + user + model and we skip the login form entirely.
  useEffect(() => {
    if (apiClient.isAuthenticated()) return;
    apiClient.restoreSession().then((restored) => {
      if (restored) {
        setUser(apiClient.getUser());
        setModelId(apiClient.getModelId());
        setModelName(apiClient.getModelName());
        setAuthFailed(false);
      }
    }).catch(() => {
      // No session to restore — user will see login form
    });
  }, []);

  // If VITE_API_KEY is set, try token exchange on mount (auto-login)
  useEffect(() => {
    if (import.meta.env.VITE_API_KEY && !apiClient.isAuthenticated()) {
      apiClient.ensureToken().catch(() => {
        // API key exchange failed — user will see login form
      });
    }
  }, []);

  // Start polling seed status and auto-retry login when seed finishes
  const startSeedPolling = useCallback((username: string, password: string) => {
    pendingCredsRef.current = { username, password };
    if (pollTimerRef.current) return; // already polling

    const poll = async () => {
      try {
        const status = await brokerApi.getSeedStatus();
        setSeedStatus(status);
        if (!status.IsLoading && status.Phase === 'Done') {
          // Seed finished loading — auto-retry login
          // Read creds BEFORE stopPolling (which clears pendingCredsRef)
          const creds = pendingCredsRef.current;
          stopPolling();
          setSeedStatus(null);
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
        // Broker might not be reachable yet — keep polling
      }
    };

    poll(); // immediate first check
    pollTimerRef.current = setInterval(poll, 2000);
  }, [stopPolling]);

  const login = useCallback(async (username: string, password: string, selectedModelId?: string) => {
    setLoading(true);
    setError(null);
    stopPolling();
    setSeedStatus(null);
    try {
      const u = await apiClient.login(username, password, selectedModelId);
      setUser(u);
      setModelId(apiClient.getModelId());
      setModelName(apiClient.getModelName());
      setAvailableModels(null);
      setAuthFailed(false);
    } catch (err: unknown) {
      // Check if the error contains available models (multi-model, no selection)
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
      // "No models loaded" can mean two very different things:
      //   (a) a seed is currently being loaded at startup → poll and auto-retry
      //   (b) the broker has nothing in its library and never will on its own
      //       → must surface an actionable error instead of silently polling a
      //         seed-status that will never reach Phase=Done (Bug #5324).
      // Disambiguate by reading SeedLoadingStatus before deciding.
      if (msg.includes('No models loaded')) {
        try {
          const status = await brokerApi.getSeedStatus();
          if (status.IsLoading) {
            setError(null);
            setSeedStatus(status);
            startSeedPolling(username, password);
            return;
          }
        } catch { /* fall through to actionable error */ }
        setError(
          'No models on broker. Drop a .seed.json into vos.Broker/seeds-library/ and reload, '
          + 'or POST /api/broker/library-seeds/<name>/load.',
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
    // Clear UI state immediately so the user sees the login form without
    // waiting for the network round-trip. The broker-side cookie clear
    // (Bug #5290) happens in apiClient.logout() and is awaited so callers
    // that want to be sure the cookie is gone (e.g. before a programmatic
    // navigation) can rely on it.
    //
    // setAuthFailed(true) is the load-bearing call for Bug #5325: it forces
    // the next render to evaluate isAuthenticated to false even though
    // apiClient.isAuthenticated() (which reads a non-React field) still
    // returns true until the awaited broker round-trip completes. login()
    // resets authFailed back to false on success.
    useModelStore.getState().clear();
    setUser(null);
    setModelId(null);
    setModelName(null);
    setAvailableModels(null);
    setError(null);
    setAuthFailed(true);
    await apiClient.logout();
  }, []);

  /** Show the seed library picker. Fetches available seeds from the library folder. */
  const switchModel = useCallback(async () => {
    setError(null);
    try {
      const seeds = await brokerApi.getLibrarySeeds();
      const asModels: ModelSummary[] = seeds.map((s) => ({
        Id: s.name,
        Name: `${s.name.replace('.seed.json', '')}  (${s.sizeMb} MB)`,
      }));
      setAvailableModels(asModels);
    } catch {
      setError('Failed to fetch library seeds');
    }
  }, []);

  /** Change the current user's password. Clears the mustChangePassword flag on success. */
  const changePassword = useCallback(async (currentPassword: string, newPassword: string) => {
    setLoading(true);
    setError(null);
    try {
      const userId = user?.Id;
      if (!userId) throw new Error('No user logged in');
      await apiClient.changePassword(userId, newPassword, currentPassword);
      // Update local user state to clear the flag
      setUser(prev => prev ? { ...prev, MustChangePassword: false } : null);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Password change failed';
      setError(msg);
      throw err;
    } finally {
      setLoading(false);
    }
  }, [user]);

  /** Load a seed file from the library folder. */
  const selectModel = useCallback(async (seedName: string) => {
    setLoading(true);
    setError(null);
    try {
      const result = await brokerApi.loadSeed(seedName);
      // The broker removed the old model from the store. Re-scope the JWT
      // to the newly loaded model (works for both login and API-key auth).
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

  /** Save the current model to the library as a seed file. */
  const saveSeed = useCallback(async (name: string) => {
    setLoading(true);
    setError(null);
    try {
      await brokerApi.saveSeed(name);
      // Refresh the seed list to show the newly saved file
      const seeds = await brokerApi.getLibrarySeeds();
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
    seedStatus,
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
