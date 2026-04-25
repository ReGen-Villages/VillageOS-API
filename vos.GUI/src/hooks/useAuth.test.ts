import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';

// ── Mocks ────────────────────────────────────────────────────────────────
// apiClient is the source of login/auth state. We capture the login mock so
// each test can shape its rejection (no-models, multi-model picker, etc).
const mockLogin = vi.fn();
const mockRestoreSession = vi.fn().mockResolvedValue(false);
const mockSetAuthRequiredCallback = vi.fn();
const mockSetUserUpdatedCallback = vi.fn();
vi.mock('../api/client', () => ({
  apiClient: {
    login: (u: string, p: string, m?: string) => mockLogin(u, p, m),
    logout: vi.fn().mockResolvedValue(undefined),
    getUser: () => null,
    getModelId: () => null,
    getModelName: () => null,
    isAuthenticated: () => false,
    setAuthRequiredCallback: (cb: () => void) => mockSetAuthRequiredCallback(cb),
    setUserUpdatedCallback: (cb: (u: unknown) => void) => mockSetUserUpdatedCallback(cb),
    restoreSession: () => mockRestoreSession(),
    ensureToken: vi.fn().mockResolvedValue(undefined),
    rescopeToModel: vi.fn().mockResolvedValue(undefined),
  },
  AuthRequiredError: class extends Error {},
}));

const mockGetSeedStatus = vi.fn();
vi.mock('../api/brokerApi', () => ({
  brokerApi: {
    getSeedStatus: () => mockGetSeedStatus(),
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
// surfaces when the broker returns 400 { error: "No models loaded" }.
function noModelsError(): Error & { body: string } {
  const err = new Error('No models loaded') as Error & { body: string };
  err.body = JSON.stringify({ error: 'No models loaded' });
  return err;
}

describe('useAuthState login: no-models-loaded handling (Bug #5324)', () => {
  beforeEach(() => {
    mockLogin.mockReset();
    mockGetSeedStatus.mockReset();
    mockRestoreSession.mockReset().mockResolvedValue(false);
    vi.useFakeTimers({ shouldAdvanceTime: true });
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it('surfaces an actionable error when no seed has ever loaded', async () => {
    // Broker rejects login because no models are loaded
    mockLogin.mockRejectedValue(noModelsError());
    // SeedLoadingStatus default: IsLoading=false, Phase="" (never started)
    mockGetSeedStatus.mockResolvedValue({
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

  it('starts seed-status polling (and clears the error) when a seed is actively loading', async () => {
    mockLogin.mockRejectedValueOnce(noModelsError());
    mockGetSeedStatus.mockResolvedValue({
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

    // Seed-loading path: error suppressed, seedStatus surfaced for the banner
    await waitFor(() => {
      expect(result.current.seedStatus?.IsLoading).toBe(true);
    });
    expect(result.current.error).toBeNull();
    expect(result.current.seedStatus?.CurrentFile).toBe('MarthasVineyard.seed.json');
  });
});
