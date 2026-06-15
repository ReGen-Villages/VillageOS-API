import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';

// ── Mocks ────────────────────────────────────────────────────────────────
// apiClient is the source of login/auth state. We capture the login mock so
// each test can shape its rejection (no-models, multi-model picker, etc).
const mockLogin = vi.fn();
const mockRestoreSession = vi.fn().mockResolvedValue(false);
const mockSetAuthRequiredCallback = vi.fn();
const mockSetUserUpdatedCallback = vi.fn();
// Stateful mocks for getUser / isAuthenticated so individual tests can simulate
// "already logged in" state and exercise the logout flow. The real apiClient
// reads these from a non-React field — that's exactly the source of Bug #5325.
let mockInitialUser: { Id: string; Username: string; Role: string } | null = null;
let mockIsAuth = false;
vi.mock('../api/client', () => ({
  apiClient: {
    login: (u: string, p: string, m?: string) => mockLogin(u, p, m),
    logout: vi.fn().mockResolvedValue(undefined),
    getUser: () => mockInitialUser,
    getModelId: () => null,
    getModelName: () => null,
    isAuthenticated: () => mockIsAuth,
    setAuthRequiredCallback: (cb: () => void) => mockSetAuthRequiredCallback(cb),
    setUserUpdatedCallback: (cb: (u: unknown) => void) => mockSetUserUpdatedCallback(cb),
    restoreSession: () => mockRestoreSession(),
    ensureToken: vi.fn().mockResolvedValue(undefined),
    rescopeToModel: vi.fn().mockResolvedValue(undefined),
  },
  AuthRequiredError: class extends Error {},
}));

const mockGetStartupProgress = vi.fn();
vi.mock('../api/myceliumApi', () => ({
  myceliumApi: {
    getStartupStatus: () => mockGetStartupProgress(),
    getLibrarySeeds: vi.fn().mockResolvedValue([]),
    loadSeed: vi.fn(),
    saveSeed: vi.fn(),
  },
}));

vi.mock('../stores/modelStore', () => ({
  useModelStore: { getState: () => ({ clear: vi.fn() }) },
}));

import { useAuthState } from './useAuth';

// Helper: shape an ApiError-ish rejection that matches what apiClient.login
// surfaces when Mycelium returns 400 { error: "No models loaded" }.
function noModelsError(): Error & { body: string } {
  const err = new Error('No models loaded') as Error & { body: string };
  err.body = JSON.stringify({ error: 'No models loaded' });
  return err;
}

describe('useAuthState login: no-models-loaded handling (Bug #5324)', () => {
  beforeEach(() => {
    mockLogin.mockReset();
    mockGetStartupProgress.mockReset();
    mockRestoreSession.mockReset().mockResolvedValue(false);
    mockInitialUser = null;
    mockIsAuth = false;
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('surfaces an actionable error when no seed has ever loaded', async () => {
    // Mycelium rejects login because no models are loaded
    mockLogin.mockRejectedValue(noModelsError());
    // SeedLoadingStatus default: IsLoading=false, Phase="" (never started)
    mockGetStartupProgress.mockResolvedValue({
      IsLoading: false,
      CurrentFile: '',
      Phase: '',
      ThingsLoaded: 0,
      RelationshipsLoaded: 0,
    });

    const { result } = renderHook(() => useAuthState());

    await act(async () => {
      try { await result.current.login('admin', 'admin'); } catch { /* expected */ }
    });

    await waitFor(() => {
      expect(result.current.error).not.toBeNull();
    });
    // The error should mention models / seeds so the user has somewhere to go
    expect(result.current.error?.toLowerCase()).toMatch(/no models|no seeds|seed/);
    expect(result.current.loading).toBe(false);
    // No silent success — user is still unauthenticated
    expect(result.current.isAuthenticated).toBe(false);
  });

  it('starts startup-status polling (and clears the error) when a seed is actively loading', async () => {
    mockLogin.mockRejectedValueOnce(noModelsError());
    mockGetStartupProgress.mockResolvedValue({
      IsLoading: true,
      CurrentFile: 'MarthasVineyard.seed.json',
      Phase: 'Deserializing',
      ThingsLoaded: 1234,
      RelationshipsLoaded: 56,
    });

    const { result } = renderHook(() => useAuthState());

    await act(async () => {
      try { await result.current.login('admin', 'admin'); } catch { /* expected */ }
    });

    // Seed-loading path: error suppressed, startupProgress surfaced for the banner
    await waitFor(() => {
      expect(result.current.startupProgress?.IsLoading).toBe(true);
    });
    expect(result.current.error).toBeNull();
    expect(result.current.startupProgress?.CurrentFile).toBe('MarthasVineyard.seed.json');
  });
});

describe('useAuthState logout: forces a re-render gate (Bug #5325)', () => {
  beforeEach(() => {
    mockLogin.mockReset();
    mockGetStartupProgress.mockReset();
    mockRestoreSession.mockReset().mockResolvedValue(false);
    // Simulate "already logged in" — the hook reads getUser() at mount and
    // isAuthenticated() at every render. The latter mirrors apiClient.token,
    // a non-React field whose changes don't trigger re-renders by themselves.
    mockInitialUser = { Id: 'u1', Username: 'admin', Role: 'admin' };
    mockIsAuth = true;
  });

  it('flips isAuthenticated to false synchronously when logout is called', async () => {
    const { result } = renderHook(() => useAuthState());
    expect(result.current.isAuthenticated).toBe(true);

    await act(async () => {
      await result.current.logout();
    });

    // The bug: with apiClient.isAuthenticated() still returning true (we
    // deliberately leave mockIsAuth=true to mirror the real-world race where
    // the in-memory token field clears AFTER the awaited Mycelium round-trip),
    // the hook must still flip isAuthenticated to false on its own React state
    // — otherwise App.tsx never re-renders to the login form.
    expect(result.current.isAuthenticated).toBe(false);
    expect(result.current.user).toBeNull();
  });
});
