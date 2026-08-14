import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { ApiError, AuthRequiredError, apiClient } from './client';

// Helper to create a mock Response
function mockResponse(status: number, body: unknown): Response {
  const text = typeof body === 'string' ? body : JSON.stringify(body);
  return {
    ok: status >= 200 && status < 300,
    status,
    text: () => Promise.resolve(text),
    json: () => Promise.resolve(typeof body === 'string' ? JSON.parse(body) : body),
  } as Response;
}

const tokenResponse = {
  token: 'test-jwt-token',
  user: { Id: 'user-1', Username: 'testuser', Role: 'admin', MustChangePassword: false },
  model: { Id: 'model-1', Name: 'test-model' },
};

describe('ApiError', () => {
  it('extracts message from JSON body with "message" field', () => {
    const err = new ApiError(400, '{"message":"Bad input"}');
    expect(err.message).toBe('Bad input');
    expect(err.status).toBe(400);
    expect(err.body).toBe('{"message":"Bad input"}');
  });

  it('extracts message from JSON body with "error" field', () => {
    const err = new ApiError(500, '{"error":"Internal failure"}');
    expect(err.message).toBe('Internal failure');
  });

  it('prefers "error" over "message" when both present', () => {
    const err = new ApiError(422, '{"error":"err","message":"msg"}');
    expect(err.message).toBe('err');
  });

  it('falls back to raw body for non-JSON', () => {
    const err = new ApiError(503, 'Service Unavailable');
    expect(err.message).toBe('Service Unavailable');
  });

  it('falls back to status code when body is empty', () => {
    const err = new ApiError(404, '');
    expect(err.message).toBe('Request failed (404)');
  });

  it('preserves status and body properties', () => {
    const err = new ApiError(401, '{"message":"Unauthorized"}');
    expect(err.status).toBe(401);
    expect(err.body).toBe('{"message":"Unauthorized"}');
    expect(err.name).toBe('ApiError');
  });

  it('is an instance of Error', () => {
    const err = new ApiError(500, 'oops');
    expect(err).toBeInstanceOf(Error);
  });
});

describe('AuthRequiredError', () => {
  it('has correct name and message', () => {
    const err = new AuthRequiredError();
    expect(err.name).toBe('AuthRequiredError');
    expect(err.message).toBe('Authentication required');
    expect(err).toBeInstanceOf(Error);
  });
});

describe('ApiClient', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    apiClient.logout(); // reset state
    fetchSpy = vi.spyOn(globalThis, 'fetch');
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  describe('login', () => {
    it('stores token, user, and model from response', async () => {
      fetchSpy.mockResolvedValueOnce(mockResponse(200, tokenResponse));

      const user = await apiClient.login('testuser', 'pass');

      expect(user.Username).toBe('testuser');
      expect(user.Role).toBe('admin');
      expect(user.MustChangePassword).toBe(false);
      expect(apiClient.isAuthenticated()).toBe(true);
      expect(apiClient.getUser()).toEqual(tokenResponse.user);
      expect(apiClient.getModelId()).toBe('model-1');
      expect(apiClient.getModelName()).toBe('test-model');
    });

    it('preserves MustChangePassword flag from response', async () => {
      const resp = { ...tokenResponse, user: { ...tokenResponse.user, MustChangePassword: true } };
      fetchSpy.mockResolvedValueOnce(mockResponse(200, resp));

      const user = await apiClient.login('testuser', 'pass');

      expect(user.MustChangePassword).toBe(true);
    });

    it('throws ApiError on bad credentials', async () => {
      fetchSpy.mockResolvedValueOnce(mockResponse(401, '{"error":"Invalid username or password"}'));

      await expect(apiClient.login('bad', 'creds')).rejects.toThrow('Invalid username or password');
      expect(apiClient.isAuthenticated()).toBe(false);
    });

    it('sends modelId when provided', async () => {
      fetchSpy.mockResolvedValueOnce(mockResponse(200, tokenResponse));

      await apiClient.login('testuser', 'pass', 'model-1');

      const call = fetchSpy.mock.calls[0];
      const body = JSON.parse(call[1]?.body as string);
      expect(body.ModelId).toBe('model-1');
    });
  });

  describe('logout', () => {
    it('clears all auth state', async () => {
      fetchSpy.mockResolvedValueOnce(mockResponse(200, tokenResponse));
      await apiClient.login('testuser', 'pass');
      expect(apiClient.isAuthenticated()).toBe(true);

      // Logout also calls Mycelium to clear the session cookie.
      fetchSpy.mockResolvedValueOnce(mockResponse(200, '{}'));
      await apiClient.logout();

      expect(apiClient.isAuthenticated()).toBe(false);
      expect(apiClient.getUser()).toBeNull();
      expect(apiClient.getModelId()).toBeNull();
      expect(apiClient.getModelName()).toBeNull();
    });

    it('calls POST /api/auth/session/logout on Mycelium (Bug #5290)', async () => {
      fetchSpy.mockResolvedValueOnce(mockResponse(200, tokenResponse));
      await apiClient.login('testuser', 'pass');
      fetchSpy.mockClear();

      fetchSpy.mockResolvedValueOnce(mockResponse(200, '{}'));
      await apiClient.logout();

      const calls = fetchSpy.mock.calls as ReadonlyArray<[string, RequestInit?]>;
      const logoutCall = calls.find((c) => String(c[0]).endsWith('/api/auth/session/logout'));
      expect(logoutCall, 'expected a POST to /api/auth/session/logout').toBeDefined();
      expect(logoutCall![1]?.method).toBe('POST');
      const authHeader = (logoutCall![1]?.headers as Record<string, string> | undefined)?.Authorization;
      expect(authHeader).toMatch(/^Bearer /);
    });

    it('clears local state even when Mycelium logout fails', async () => {
      fetchSpy.mockResolvedValueOnce(mockResponse(200, tokenResponse));
      await apiClient.login('testuser', 'pass');

      fetchSpy.mockRejectedValueOnce(new Error('network down'));
      await apiClient.logout();

      expect(apiClient.isAuthenticated()).toBe(false);
      expect(apiClient.getUser()).toBeNull();
    });

    it('skips Mycelium call when no token is stored', async () => {
      // Fresh client, never logged in — logout should be a pure no-op.
      fetchSpy.mockClear();
      await apiClient.logout();

      expect(fetchSpy).not.toHaveBeenCalled();
    });
  });

  describe('ensureToken', () => {
    it('returns cached token when valid', async () => {
      fetchSpy.mockResolvedValueOnce(mockResponse(200, tokenResponse));
      await apiClient.login('testuser', 'pass');

      const token = await apiClient.ensureToken();
      expect(token).toBe('test-jwt-token');
      // Should NOT have made a second fetch call
      expect(fetchSpy).toHaveBeenCalledTimes(1);
    });

    it('fires onAuthRequired when no token and no API key', async () => {
      const callback = vi.fn();
      apiClient.setAuthRequiredCallback(callback);

      await expect(apiClient.ensureToken()).rejects.toThrow(AuthRequiredError);
      expect(callback).toHaveBeenCalledTimes(1);
    });
  });

  describe('mintStreamToken', () => {
    it('asks the mint route for one, carrying the sign-in token in a header', async () => {
      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse))
        .mockResolvedValueOnce(mockResponse(200, { token: 'a-stream-token' }));
      await apiClient.login('testuser', 'pass');

      const streamToken = await apiClient.mintStreamToken();

      expect(streamToken).toBe('a-stream-token');
      const [url, init] = fetchSpy.mock.calls[1] as [string, RequestInit];
      expect(url).toContain('/api/auth/stream-token');
      expect(init.method).toBe('POST');
      expect((init.headers as Record<string, string>).Authorization).toBe('Bearer test-jwt-token');
    });

    it('asks for a new one every time, because each is meant to go stale', async () => {
      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse))
        .mockResolvedValueOnce(mockResponse(200, { token: 'first' }))
        .mockResolvedValueOnce(mockResponse(200, { token: 'second' }));
      await apiClient.login('testuser', 'pass');

      expect(await apiClient.mintStreamToken()).toBe('first');
      expect(await apiClient.mintStreamToken()).toBe('second');
    });

    it('reports a refused mint as authentication required rather than returning nothing', async () => {
      const callback = vi.fn();
      apiClient.setAuthRequiredCallback(callback);
      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse))
        .mockResolvedValueOnce(mockResponse(401, '{"error":"Unauthorized"}'));
      await apiClient.login('testuser', 'pass');

      await expect(apiClient.mintStreamToken()).rejects.toThrow(AuthRequiredError);
      expect(callback).toHaveBeenCalledTimes(1);
    });
  });

  describe('changePassword', () => {
    it('sends password change request and clears MustChangePassword flag', async () => {
      const loginResp = { ...tokenResponse, user: { ...tokenResponse.user, MustChangePassword: true } };
      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, loginResp)) // login
        .mockResolvedValueOnce(mockResponse(200, { message: 'Password changed' })); // changePassword

      await apiClient.login('testuser', 'pass');
      expect(apiClient.getUser()!.MustChangePassword).toBe(true);

      await apiClient.changePassword('user-1', 'newpass', 'oldpass');

      expect(apiClient.getUser()!.MustChangePassword).toBe(false);

      // Verify the PUT request body
      const putCall = fetchSpy.mock.calls[1];
      expect(putCall[0]).toContain('/api/auth/users/user-1/password');
      expect(putCall[1]?.method).toBe('PUT');
      const body = JSON.parse(putCall[1]?.body as string);
      expect(body.NewPassword).toBe('newpass');
      expect(body.CurrentPassword).toBe('oldpass');
    });

    it('omits CurrentPassword when not provided', async () => {
      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse)) // login
        .mockResolvedValueOnce(mockResponse(200, { message: 'Password changed' })); // changePassword

      await apiClient.login('testuser', 'pass');
      await apiClient.changePassword('user-1', 'newpass');

      const putCall = fetchSpy.mock.calls[1];
      const body = JSON.parse(putCall[1]?.body as string);
      expect(body.NewPassword).toBe('newpass');
      expect(body.CurrentPassword).toBeUndefined();
    });

    it('throws AuthRequiredError on 401', async () => {
      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse)) // login
        .mockResolvedValueOnce(mockResponse(401, '{"error":"Unauthorized"}')); // changePassword

      await apiClient.login('testuser', 'pass');
      await expect(apiClient.changePassword('user-1', 'newpass', 'wrong')).rejects.toThrow(AuthRequiredError);
    });
  });

  describe('token refresh', () => {
    it('schedules refresh after login and refreshes token', async () => {
      vi.useFakeTimers();

      const refreshedResponse = {
        ...tokenResponse,
        token: 'refreshed-jwt-token',
        user: { ...tokenResponse.user, Username: 'testuser' },
      };

      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse)) // login
        .mockResolvedValueOnce(mockResponse(200, refreshedResponse)); // refresh

      await apiClient.login('testuser', 'pass');
      expect(apiClient.isAuthenticated()).toBe(true);

      // Advance past 80% of the 25-minute expiry (20 minutes)
      await vi.advanceTimersByTimeAsync(20 * 60 * 1000 + 1000);

      // Refresh should have been called
      expect(fetchSpy).toHaveBeenCalledTimes(2);
      const refreshCall = fetchSpy.mock.calls[1];
      expect(refreshCall[0]).toContain('/api/auth/refresh');
      expect(refreshCall[1]?.method).toBe('POST');

      // Token should be updated
      expect(apiClient.isAuthenticated()).toBe(true);
    });

    it('fires onAuthRequired on refresh failure', async () => {
      vi.useFakeTimers();

      const callback = vi.fn();
      apiClient.setAuthRequiredCallback(callback);

      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse)) // login
        .mockResolvedValueOnce(mockResponse(401, '{"error":"Token expired"}')); // refresh fails

      await apiClient.login('testuser', 'pass');

      // Advance past 80% of expiry
      await vi.advanceTimersByTimeAsync(20 * 60 * 1000 + 1000);

      expect(callback).toHaveBeenCalledTimes(1);
      expect(apiClient.isAuthenticated()).toBe(false);
    });

    it('calls onUserUpdated callback after refresh', async () => {
      vi.useFakeTimers();

      const userUpdated = vi.fn();
      apiClient.setUserUpdatedCallback(userUpdated);

      const refreshedResponse = {
        ...tokenResponse,
        token: 'refreshed-jwt-token',
      };

      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse)) // login
        .mockResolvedValueOnce(mockResponse(200, refreshedResponse)); // refresh

      await apiClient.login('testuser', 'pass');

      await vi.advanceTimersByTimeAsync(20 * 60 * 1000 + 1000);

      expect(userUpdated).toHaveBeenCalledTimes(1);
      expect(userUpdated).toHaveBeenCalledWith(expect.objectContaining({ Username: 'testuser' }));
    });

    it('cancels refresh timer on logout', async () => {
      vi.useFakeTimers();

      fetchSpy.mockResolvedValueOnce(mockResponse(200, tokenResponse)); // login
      fetchSpy.mockResolvedValueOnce(mockResponse(200, '{}'));          // logout → session/logout

      await apiClient.login('testuser', 'pass');
      await apiClient.logout();

      // Advance past when refresh would fire — should NOT call fetch again
      await vi.advanceTimersByTimeAsync(25 * 60 * 1000);

      // Login + logout, no refresh call
      expect(fetchSpy).toHaveBeenCalledTimes(2);
      const urls = fetchSpy.mock.calls.map((c: unknown[]) => String(c[0]));
      expect(urls.some((u: string) => u.endsWith('/api/auth/refresh'))).toBe(false);
    });
  });

  describe('assertOk (via get)', () => {
    it('fires onAuthRequired on 401 response', async () => {
      const callback = vi.fn();
      apiClient.setAuthRequiredCallback(callback);

      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse)) // login
        .mockResolvedValueOnce(mockResponse(401, '{"error":"Unauthorized"}')); // get

      await apiClient.login('testuser', 'pass');
      await expect(apiClient.get('/api/things')).rejects.toThrow(AuthRequiredError);
      expect(callback).toHaveBeenCalledTimes(1);
    });

    it('throws ApiError on non-401 failures', async () => {
      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse)) // login
        .mockResolvedValueOnce(mockResponse(500, '{"error":"Server error"}')); // get

      await apiClient.login('testuser', 'pass');
      await expect(apiClient.get('/api/things')).rejects.toThrow('Server error');
    });
  });

  describe('switchModel', () => {
    it('updates model context and schedules refresh', async () => {
      const switchResp = {
        ...tokenResponse,
        token: 'switched-token',
        model: { Id: 'model-2', Name: 'other-model' },
      };

      fetchSpy
        .mockResolvedValueOnce(mockResponse(200, tokenResponse)) // login
        .mockResolvedValueOnce(mockResponse(200, switchResp)); // switchModel

      await apiClient.login('testuser', 'pass');
      const user = await apiClient.switchModel('model-2');

      expect(user.Username).toBe('testuser');
      expect(apiClient.getModelId()).toBe('model-2');
      expect(apiClient.getModelName()).toBe('other-model');
    });
  });
});
