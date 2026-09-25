import type { ModelSummary } from '../types/vos';
import i18n from '../i18n';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';
const API_KEY = import.meta.env.VITE_API_KEY || '';

/** Names this program to the broker, which says which one a person used when it tells the people the
 *  model names that they signed in, signed out or acted. */
const PROGRAM_HEADERS = { 'X-Vos-Client': 'Trellis' } as const;

export interface AuthenticatedUser {
  Id: string;
  Username: string;
  Role: string;
  MustChangePassword: boolean;
}

class ApiClient {
  private token: string | null = null;
  private tokenExpiry: Date | null = null;
  private currentUser: AuthenticatedUser | null = null;
  private currentModelId: string | null = null;
  private currentModelName: string | null = null;
  private onAuthenticationRequired: (() => void) | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;
  private onUserUpdated: ((user: AuthenticatedUser) => void) | null = null;

  setUserUpdatedCallback(callback: (user: AuthenticatedUser) => void) {
    this.onUserUpdated = callback;
  }

  setAuthenticationRequiredCallback(callback: () => void) {
    this.onAuthenticationRequired = callback;
  }

  async fetchModels(): Promise<ModelSummary[]> {
    const response = await fetch(`${BASE_URL}/api/models`, {
      headers: await this.headers(),
      credentials: 'include',
    });
    if (!response.ok) throw new ApiError(response.status, await response.text());
    return response.json();
  }

  private applyTokenResponse(data: { token: string; user: AuthenticatedUser; model?: { Id: string; Name: string } }) {
    this.currentUser = data.user as AuthenticatedUser;
    if (data.model) {
      this.currentModelId = data.model.Id;
      this.currentModelName = data.model.Name;
    }
    this.applyToken(data.token);
  }

  private applyToken(token: string) {
    this.token = token;
    this.tokenExpiry = new Date(Date.now() + 25 * 60 * 1000);
    this.scheduleRefresh();
  }

  private forgetToken() {
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = null;
    }
    this.token = null;
    this.tokenExpiry = null;
  }

  async login(username: string, password: string, modelId?: string): Promise<AuthenticatedUser> {
    const body: Record<string, string> = { Username: username, Password: password };
    if (modelId) body.ModelId = modelId;

    const response = await fetch(`${BASE_URL}/api/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...PROGRAM_HEADERS },
      credentials: 'include',
      body: JSON.stringify(body),
    });
    if (!response.ok) {
      const text = await response.text();
      throw new ApiError(response.status, text);
    }
    const data = await response.json();
    this.applyTokenResponse(data);
    return this.currentUser!;
  }

  /** Restore in-memory state from the HttpOnly session cookie. Returns false if no valid session. */
  async restoreSession(): Promise<boolean> {
    try {
      const response = await fetch(`${BASE_URL}/api/auth/restore-session`, {
        credentials: 'include',
      });
      if (!response.ok) return false;
      const data = await response.json();
      this.applyTokenResponse(data);
      return true;
    } catch {
      return false;
    }
  }

  async switchModel(modelId: string): Promise<AuthenticatedUser> {
    const response = await fetch(`${BASE_URL}/api/auth/switch-model`, {
      method: 'POST',
      headers: await this.headers(),
      credentials: 'include',
      body: JSON.stringify({ ModelId: modelId }),
    });
    if (!response.ok) throw new ApiError(response.status, await response.text());
    const data = await response.json();
    this.applyTokenResponse(data);
    return this.currentUser!;
  }

  /**
   * Re-scope the current session to a different model. User tokens switch-model
   * for a new JWT; API-key tokens invalidate so the next ensureToken() re-exchanges.
   */
  async rescopeToModel(modelId: string): Promise<void> {
    if (API_KEY) {
      this.forgetToken();
      this.currentModelId = null;
      this.currentModelName = null;
      await this.ensureToken();
    } else {
      await this.switchModel(modelId);
    }
  }

  /**
   * Clear credentials and cancel pending refresh. Must clear the HttpOnly session
   * cookie too, else restoreSession() silently signs the user back in.
   * Best-effort: local state clears even if the network call fails.
   */
  async logout(): Promise<void> {
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = null;
    }

    if (this.token) {
      try {
        await fetch(`${BASE_URL}/api/auth/session/logout`, {
          method: 'POST',
          headers: { Authorization: `Bearer ${this.token}`, ...PROGRAM_HEADERS },
          credentials: 'include',
        });
      } catch {
        // Best-effort — network failure shouldn't leave the UI signed in.
      }
    }

    this.forgetToken();
    this.currentUser = null;
    this.currentModelId = null;
    this.currentModelName = null;
  }

  /** Schedule a background token refresh at 80% of the token's remaining lifetime. */
  private scheduleRefresh() {
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    if (!this.tokenExpiry) return;

    const millisecondsUntilExpiry = this.tokenExpiry.getTime() - Date.now();
    const refreshAt = Math.max(millisecondsUntilExpiry * 0.8, 10_000); // at least 10s from now

    this.refreshTimer = setTimeout(async () => {
      try {
        await this.refreshToken();
      } catch {
        void this.logout();
        if (this.onAuthenticationRequired) this.onAuthenticationRequired();
      }
    }, refreshAt);
  }

  private async refreshToken(): Promise<void> {
    if (!this.token) throw new Error('No token to refresh');

    const response = await fetch(`${BASE_URL}/api/auth/refresh`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${this.token}`,
      },
      credentials: 'include',
    });
    if (!response.ok) throw new Error(`Refresh failed: ${response.status}`);

    const data = await response.json();
    this.applyTokenResponse(data);
    if (this.onUserUpdated) this.onUserUpdated(this.currentUser!);
  }

  /**
   * Non-admin users must provide their current password. Changing your own password answers with a
   * token that no longer says the password must change; the broker refuses the old one everywhere.
   * The answer has no token when the broker could not mint one, and then signing in again is the
   * only way on, so the old token is dropped rather than kept.
   */
  async changePassword(userId: string, newPassword: string, currentPassword?: string): Promise<void> {
    const body: Record<string, string> = { NewPassword: newPassword };
    if (currentPassword) body.CurrentPassword = currentPassword;

    await this.action('change a password', async () => {
      const response = await fetch(`${BASE_URL}/api/auth/users/${userId}/password`, {
        method: 'PUT',
        headers: await this.headers(),
        credentials: 'include',
        body: JSON.stringify(body),
      });
      await this.assertOk(response);
      const { token } = await response.json();
      if (token) this.applyToken(token);
      else this.forgetToken();
    });
    if (this.currentUser) {
      this.currentUser = { ...this.currentUser, MustChangePassword: false };
    }
  }

  getUser(): AuthenticatedUser | null {
    return this.currentUser;
  }

  getModelId(): string | null {
    return this.currentModelId;
  }

  getModelName(): string | null {
    return this.currentModelName;
  }

  isAuthenticated(): boolean {
    return !!(this.token && this.tokenExpiry && new Date() < this.tokenExpiry);
  }

  /**
   * Ensure we have a valid token. Authentication modes: existing login token used directly;
   * else VITE_API_KEY exchanged for a short-lived JWT; else authentication is required.
   */
  async ensureToken(): Promise<string> {
    if (this.token && this.tokenExpiry && new Date() < this.tokenExpiry) {
      return this.token;
    }

    if (API_KEY) {
      const parameters = this.currentModelId ? `?modelId=${this.currentModelId}` : '';
      const response = await fetch(`${BASE_URL}/api/auth/token${parameters}`, {
        method: 'POST',
        headers: { 'X-API-Key': API_KEY },
      });
      if (!response.ok) {
        this.token = null;
        this.tokenExpiry = null;
        if (this.onAuthenticationRequired) this.onAuthenticationRequired();
        throw new AuthenticationRequiredError();
      }
      const data = await response.json();
      this.token = data.token;
      this.tokenExpiry = new Date(Date.now() + 4 * 60 * 1000); // API key JWTs are 5min
      return this.token!;
    }

    if (this.onAuthenticationRequired) this.onAuthenticationRequired();
    throw new AuthenticationRequiredError();
  }

  /**
   * Mints the credential an EventSource address may carry, since EventSource cannot set a request
   * header and an address is recorded where a header is not. Never cached: one per stream open,
   * reconnects included, so the copy left in a log is stale by the time anyone reads it.
   */
  async mintStreamToken(): Promise<string> {
    const response = await fetch(`${BASE_URL}/api/auth/stream-token`, {
      method: 'POST',
      headers: await this.headers(),
      credentials: 'include',
    });
    await this.assertOk(response);
    const data = await response.json();
    return data.token as string;
  }

  private async headers(): Promise<HeadersInit> {
    const token = await this.ensureToken();
    return {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token}`,
      ...PROGRAM_HEADERS,
    };
  }

  /**
   * Runs a change a person asked for and then tells the broker about it, so the people the model names
   * hear of it. Only a person's own request goes through here: a read, and anything Trellis sends on
   * its own, does not. The report is not awaited and its failure is swallowed, because the person asked
   * for the change, not for the report.
   */
  async action<T>(description: string, request: () => Promise<T>): Promise<T> {
    let succeeded = false;
    try {
      const result = await request();
      succeeded = true;
      return result;
    } finally {
      void this.reportAction(description, succeeded);
    }
  }

  async reportAction(description: string, succeeded: boolean): Promise<void> {
    try {
      await fetch(`${BASE_URL}/api/operator-activity`, {
        method: 'POST',
        headers: await this.headers(),
        credentials: 'include',
        body: JSON.stringify({ Kind: 'action', Description: description, Succeeded: succeeded }),
      });
    } catch {
      // Nothing to tell the person: their change stands either way.
    }
  }

  private async assertOk(response: Response): Promise<void> {
    if (response.status === 401) {
      this.token = null;
      this.tokenExpiry = null;
      if (this.onAuthenticationRequired) this.onAuthenticationRequired();
      throw new AuthenticationRequiredError();
    }
    if (!response.ok) throw new ApiError(response.status, await response.text());
  }

  /** `signal` lets a caller abandon a superseded round of requests; without it a fan-out
   *  that is already stale keeps competing for connections with the round that replaced it. */
  async get<T>(path: string, signal?: AbortSignal): Promise<T> {
    const response = await fetch(`${BASE_URL}${path}`, {
      headers: await this.headers(),
      credentials: 'include',
      signal,
    });
    await this.assertOk(response);
    // Guard against empty response bodies (e.g. 200 with 0 bytes) —
    // response.json() throws SyntaxError on empty input.
    if (response.status === 204) return ([] as unknown) as T;
    try {
      return await response.json();
    } catch {
      return ([] as unknown) as T;
    }
  }

  async getText(path: string): Promise<string> {
    const response = await fetch(`${BASE_URL}${path}`, {
      headers: await this.headers(),
      credentials: 'include',
    });
    await this.assertOk(response);
    return response.text();
  }

  /**
   * Fetches binary bytes (e.g. the Fragments .frag artifact).
   * Returns null on 404 so callers can distinguish "not yet ingested" from real errors.
   */
  async getBytes(path: string): Promise<ArrayBuffer | null> {
    const token = await this.ensureToken();
    const response = await fetch(`${BASE_URL}${path}`, {
      headers: { Authorization: `Bearer ${token}` },
      credentials: 'include',
    });
    if (response.status === 404) return null;
    await this.assertOk(response);
    return response.arrayBuffer();
  }

  async post<T>(path: string, body?: unknown): Promise<T> {
    const response = await fetch(`${BASE_URL}${path}`, {
      method: 'POST',
      headers: await this.headers(),
      credentials: 'include',
      body: body !== undefined ? JSON.stringify(body) : undefined,
    });
    await this.assertOk(response);
    return response.json();
  }

  async put<T>(path: string, body: unknown): Promise<T> {
    const response = await fetch(`${BASE_URL}${path}`, {
      method: 'PUT',
      headers: await this.headers(),
      credentials: 'include',
      body: JSON.stringify(body),
    });
    await this.assertOk(response);
    return response.json();
  }

  async del<T>(path: string): Promise<T> {
    const response = await fetch(`${BASE_URL}${path}`, {
      method: 'DELETE',
      headers: await this.headers(),
      credentials: 'include',
    });
    await this.assertOk(response);
    return response.json();
  }

}

export class ApiError extends Error {
  status: number;
  body: string;

  constructor(status: number, body: string) {
    let message: string;
    try {
      const parsed = JSON.parse(body);
      message = parsed.error || parsed.message || body;
    } catch {
      message = body;
    }
    super(message || i18n.t('common.requestFailed', { status }));
    this.name = 'ApiError';
    this.status = status;
    this.body = body;
  }
}

export class AuthenticationRequiredError extends Error {
  constructor() {
    super(i18n.t('authentication.required'));
    this.name = 'AuthRequiredError';
  }
}

export const apiClient = new ApiClient();
