import type { ModelSummary } from '../types/vos';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';
const API_KEY = import.meta.env.VITE_API_KEY || '';

export interface AuthUser {
  Id: string;
  Username: string;
  Role: string;
  MustChangePassword: boolean;
}

class ApiClient {
  private token: string | null = null;
  private tokenExpiry: Date | null = null;
  private currentUser: AuthUser | null = null;
  private currentModelId: string | null = null;
  private currentModelName: string | null = null;
  private onAuthRequired: (() => void) | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private onUserUpdated: ((user: AuthUser) => void) | null = null;

  /** Register a callback for when the user object is updated (e.g. after token refresh). */
  setUserUpdatedCallback(cb: (user: AuthUser) => void) {
    this.onUserUpdated = cb;
  }

  /** Register a callback to trigger when authentication is needed. */
  setAuthRequiredCallback(cb: () => void) {
    this.onAuthRequired = cb;
  }

  /** Fetch available models (does not require model context in token). */
  async fetchModels(): Promise<ModelSummary[]> {
    const resp = await fetch(`${BASE_URL}/api/models`, {
      headers: await this.headers(),
      credentials: 'include',
    });
    if (!resp.ok) throw new ApiError(resp.status, await resp.text());
    return resp.json();
  }

  /** Apply a token response (shared by login, switchModel, refreshToken). */
  private applyTokenResponse(data: { token: string; user: AuthUser; model?: { Id: string; Name: string } }) {
    this.token = data.token;
    this.tokenExpiry = new Date(Date.now() + 25 * 60 * 1000);
    this.currentUser = data.user as AuthUser;
    if (data.model) {
      this.currentModelId = data.model.Id;
      this.currentModelName = data.model.Name;
    }
    this.scheduleRefresh();
  }

  /** Login with username and password. Optionally specify a model. */
  async login(username: string, password: string, modelId?: string): Promise<AuthUser> {
    const body: Record<string, string> = { Username: username, Password: password };
    if (modelId) body.ModelId = modelId;

    const resp = await fetch(`${BASE_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      credentials: 'include',
      body: JSON.stringify(body),
    });
    if (!resp.ok) {
      const text = await resp.text();
      throw new ApiError(resp.status, text);
    }
    const data = await resp.json();
    this.applyTokenResponse(data);
    return this.currentUser!;
  }

  /**
   * Attempt to restore a session from an HttpOnly cookie set during a prior login.
   * If the cookie exists and the JWT inside it is still valid, the broker returns
   * the token + user + model and we restore in-memory state without re-entering
   * credentials. Returns true if restored, false if no valid session.
   */
  async restoreSession(): Promise<boolean> {
    try {
      const resp = await fetch(`${BASE_URL}/api/auth/restore-session`, {
        credentials: 'include',
      });
      if (!resp.ok) return false;
      const data = await resp.json();
      this.applyTokenResponse(data);
      return true;
    } catch {
      return false;
    }
  }

  /**
   * Switch to a different model using the current JWT (no credentials needed).
   * Calls POST /api/auth/switch-model which issues a new token scoped to the target model.
   */
  async switchModel(modelId: string): Promise<AuthUser> {
    const resp = await fetch(`${BASE_URL}/api/auth/switch-model`, {
      method: 'POST',
      headers: await this.headers(),
      credentials: 'include',
      body: JSON.stringify({ ModelId: modelId }),
    });
    if (!resp.ok) throw new ApiError(resp.status, await resp.text());
    const data = await resp.json();
    this.applyTokenResponse(data);
    return this.currentUser!;
  }

  /**
   * Re-scope the current session to a different model.
   * For user tokens: calls switch-model to get a new JWT.
   * For API-key tokens: invalidates the cached token so ensureToken()
   * re-exchanges the API key (auto-selects the single remaining model).
   */
  async rescopeToModel(modelId: string): Promise<void> {
    if (API_KEY) {
      // API-key mode: invalidate token; next ensureToken() re-exchanges
      if (this.refreshTimer) {
        clearTimeout(this.refreshTimer);
        this.refreshTimer = null;
      }
      this.token = null;
      this.tokenExpiry = null;
      this.currentModelId = null;
      this.currentModelName = null;
      await this.ensureToken();
    } else {
      // Login mode: switch-model issues a new JWT
      await this.switchModel(modelId);
    }
  }

  /** Clear stored credentials and cancel any pending refresh. */
  logout() {
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = null;
    }
    this.token = null;
    this.tokenExpiry = null;
    this.currentUser = null;
    this.currentModelId = null;
    this.currentModelName = null;
  }

  /**
   * Schedule a background token refresh at 80% of the token's remaining lifetime.
   * On success, updates stored token/user and schedules the next refresh.
   * On failure, clears state and fires onAuthRequired.
   */
  private scheduleRefresh() {
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    if (!this.tokenExpiry) return;

    const msUntilExpiry = this.tokenExpiry.getTime() - Date.now();
    const refreshAt = Math.max(msUntilExpiry * 0.8, 10_000); // at least 10s from now

    this.refreshTimer = setTimeout(async () => {
      try {
        await this.refreshToken();
      } catch {
        // Refresh failed — auth required
        this.logout();
        if (this.onAuthRequired) this.onAuthRequired();
      }
    }, refreshAt);
  }

  /** Call POST /api/auth/refresh to get a new token with the same identity and model scope. */
  private async refreshToken(): Promise<void> {
    if (!this.token) throw new Error('No token to refresh');

    const resp = await fetch(`${BASE_URL}/api/auth/refresh`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${this.token}`,
      },
      credentials: 'include',
    });
    if (!resp.ok) throw new Error(`Refresh failed: ${resp.status}`);

    const data = await resp.json();
    this.applyTokenResponse(data);
    if (this.onUserUpdated) this.onUserUpdated(this.currentUser!);
  }

  /** Change a user's password. Non-admin users must provide their current password. */
  async changePassword(userId: string, newPassword: string, currentPassword?: string): Promise<void> {
    const body: Record<string, string> = { NewPassword: newPassword };
    if (currentPassword) body.CurrentPassword = currentPassword;

    const resp = await fetch(`${BASE_URL}/api/auth/users/${userId}/password`, {
      method: 'PUT',
      headers: await this.headers(),
      credentials: 'include',
      body: JSON.stringify(body),
    });
    await this.assertOk(resp);
    if (this.currentUser) {
      this.currentUser = { ...this.currentUser, MustChangePassword: false };
    }
  }

  /** Get the currently authenticated user, if any. */
  getUser(): AuthUser | null {
    return this.currentUser;
  }

  /** Get the current model ID. */
  getModelId(): string | null {
    return this.currentModelId;
  }

  /** Get the current model name. */
  getModelName(): string | null {
    return this.currentModelName;
  }

  /** Returns true if the client has a valid (non-expired) token. */
  isAuthenticated(): boolean {
    return !!(this.token && this.tokenExpiry && new Date() < this.tokenExpiry);
  }

  /**
   * Ensure we have a valid token. Supports three auth modes:
   * 1. VITE_API_KEY env var → exchange for short-lived JWT via X-API-Key header
   * 2. Existing token from login → use directly
   * 3. Neither → signal that auth is required
   */
  async ensureToken(): Promise<string> {
    if (this.token && this.tokenExpiry && new Date() < this.tokenExpiry) {
      return this.token;
    }

    // Try API key exchange
    if (API_KEY) {
      const params = this.currentModelId ? `?modelId=${this.currentModelId}` : '';
      const resp = await fetch(`${BASE_URL}/api/auth/token${params}`, {
        method: 'POST',
        headers: { 'X-API-Key': API_KEY },
      });
      if (!resp.ok) {
        this.token = null;
        this.tokenExpiry = null;
        if (this.onAuthRequired) this.onAuthRequired();
        throw new AuthRequiredError();
      }
      const data = await resp.json();
      this.token = data.token;
      this.tokenExpiry = new Date(Date.now() + 4 * 60 * 1000); // API key JWTs are 5min
      return this.token!;
    }

    // No token and no API key — auth is required
    if (this.onAuthRequired) this.onAuthRequired();
    throw new AuthRequiredError();
  }

  private async headers(): Promise<HeadersInit> {
    const token = await this.ensureToken();
    return {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
    };
  }

  /** Check response for auth failure (401) and redirect to login if needed. */
  private async assertOk(resp: Response): Promise<void> {
    if (resp.status === 401) {
      this.token = null;
      this.tokenExpiry = null;
      if (this.onAuthRequired) this.onAuthRequired();
      throw new AuthRequiredError();
    }
    if (!resp.ok) throw new ApiError(resp.status, await resp.text());
  }

  async get<T>(path: string): Promise<T> {
    const resp = await fetch(`${BASE_URL}${path}`, {
      headers: await this.headers(),
      credentials: 'include',
    });
    await this.assertOk(resp);
    // Guard against empty response bodies (e.g. 200 with 0 bytes) —
    // resp.json() throws SyntaxError on empty input.
    if (resp.status === 204) return ([] as unknown) as T;
    try {
      return await resp.json();
    } catch {
      return ([] as unknown) as T;
    }
  }

  async getText(path: string): Promise<string> {
    const resp = await fetch(`${BASE_URL}${path}`, {
      headers: await this.headers(),
      credentials: 'include',
    });
    await this.assertOk(resp);
    return resp.text();
  }

  /**
   * Fetches binary bytes (e.g. the Fragments .frag artifact).
   * Returns null on 404 so callers can distinguish "not yet ingested" from real errors.
   */
  async getBytes(path: string): Promise<ArrayBuffer | null> {
    const token = await this.ensureToken();
    const resp = await fetch(`${BASE_URL}${path}`, {
      headers: { Authorization: `Bearer ${token}` },
      credentials: 'include',
    });
    if (resp.status === 404) return null;
    await this.assertOk(resp);
    return resp.arrayBuffer();
  }

  async post<T>(path: string, body?: unknown): Promise<T> {
    const resp = await fetch(`${BASE_URL}${path}`, {
      method: 'POST',
      headers: await this.headers(),
      credentials: 'include',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    await this.assertOk(resp);
    return resp.json();
  }

  async put<T>(path: string, body: unknown): Promise<T> {
    const resp = await fetch(`${BASE_URL}${path}`, {
      method: 'PUT',
      headers: await this.headers(),
      credentials: 'include',
      body: JSON.stringify(body),
    });
    await this.assertOk(resp);
    return resp.json();
  }

  async del<T>(path: string): Promise<T> {
    const resp = await fetch(`${BASE_URL}${path}`, {
      method: 'DELETE',
      headers: await this.headers(),
      credentials: 'include',
    });
    await this.assertOk(resp);
    return resp.json();
  }

}

export class ApiError extends Error {
  status: number;
  body: string;

  constructor(status: number, body: string) {
    let msg: string;
    try {
      const parsed = JSON.parse(body);
      msg = parsed.error || parsed.message || body;
    } catch {
      msg = body;
    }
    super(msg || `Request failed (${status})`);
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
  }
}

export class AuthRequiredError extends Error {
  constructor() {
    super('Authentication required');
    this.name = 'AuthRequiredError';
  }
}

export const apiClient = new ApiClient();
