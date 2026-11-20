import i18n from '../i18n';

const BASE_URL = import.meta.env.VITE_BROKER_URL || '';
const API_KEY = import.meta.env.VITE_API_KEY || '';

/** Tells the broker which program a person used, which it says when it tells the people the model names
 *  that someone signed in, signed out or acted. */
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

  /** Through the broker, because the page cannot read the session cookie itself. */
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

  /** The broker must clear the session cookie too, or restoreSession signs the person straight back in. */
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
        // The page signs out even when the broker cannot be reached.
      }
    }

    this.forgetToken();
    this.currentUser = null;
    this.currentModelId = null;
    this.currentModelName = null;
  }

  private scheduleRefresh() {
    if (this.refreshTimer) clearTimeout(this.refreshTimer);
    if (!this.tokenExpiry) return;

    const millisecondsUntilExpiry = this.tokenExpiry.getTime() - Date.now();
    const refreshAt = Math.max(millisecondsUntilExpiry * 0.8, 10_000);

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

  /** Someone who is not an administrator must give their current password. The broker then refuses the old
   *  token everywhere, so it is replaced by the one answered, or dropped when there is none and the person
   *  must sign in again. */
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

  /** The sign-in for a request this client does not make itself, such as a map's tile request. */
  authorizationHeaders(): Record<string, string> {
    return this.token ? { Authorization: `Bearer ${this.token}` } : {};
  }

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
      this.tokenExpiry = new Date(Date.now() + 4 * 60 * 1000); // renewed a minute before the key's token runs out
      return this.token!;
    }

    if (this.onAuthenticationRequired) this.onAuthenticationRequired();
    throw new AuthenticationRequiredError();
  }

  /** EventSource cannot set a header, so its address carries this token, and an address is logged. A fresh one
   *  per stream opened, reconnects included, so a copy left in a log has expired before anyone reads it. */
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

  /** Reports the change, so the people the model names hear of it. Only a person's own change comes through
   *  here, never a read or anything Trellis sends by itself. The report is not awaited and its failure is
   *  ignored, because the person asked for the change, not the report. */
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

  /** `signal` lets a caller cancel a replaced round of requests, which would otherwise compete for
   *  connections with the round that replaced it. */
  async get<T>(path: string, signal?: AbortSignal): Promise<T> {
    const response = await this.send('GET', path, undefined, signal);
    // An empty body, even under 200, makes response.json() throw.
    if (response.status === 204) return ([] as unknown) as T;
    try {
      return await response.json();
    } catch {
      return ([] as unknown) as T;
    }
  }

  async getText(path: string): Promise<string> {
    return (await this.send('GET', path)).text();
  }

  /** Null on 404, so a caller can tell a file not yet ingested from a real error. */
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
    return (await this.send('POST', path, body)).json();
  }

  async put<T>(path: string, body: unknown): Promise<T> {
    return (await this.send('PUT', path, body)).json();
  }

  async del<T>(path: string): Promise<T> {
    return (await this.send('DELETE', path)).json();
  }

  private async send(method: 'GET' | 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown, signal?: AbortSignal): Promise<Response> {
    const response = await fetch(`${BASE_URL}${path}`, {
      method,
      headers: await this.headers(),
      credentials: 'include',
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal,
    });
    await this.assertOk(response);
    return response;
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
