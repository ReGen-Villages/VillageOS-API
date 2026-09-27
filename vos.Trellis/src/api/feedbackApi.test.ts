import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({ apiClient: { ensureToken: vi.fn() } }));

import { feedbackApi } from './feedbackApi';
import { apiClient } from './client';
import { FeedbackRefusedError, type FeedbackReport } from '../feedback/feedbackPanel';

const fetchMock = vi.fn();

const report: FeedbackReport = {
  application: 'Trellis',
  kind: 'bug',
  title: 'The map stays blank',
  description: '',
  screenshot: null,
  context: { pageAddress: 'https://app.example.org/', browser: 'Test', screenSize: '800 × 600', language: 'en' },
};

function answer(status: number, body: unknown) {
  return { ok: status < 400, status, json: () => Promise.resolve(body) };
}

beforeEach(() => {
  vi.clearAllMocks();
  (import.meta.env as Record<string, string>).VITE_FEEDBACK_URL = '';
  vi.stubGlobal('fetch', fetchMock);
  vi.mocked(apiClient.ensureToken).mockResolvedValue('jwt-123');
});

describe('feedbackApi', () => {
  it('posts the report to the relay on this host, carrying the sign-in token', async () => {
    fetchMock.mockResolvedValue(answer(200, { reference: 7400 }));

    const filed = await feedbackApi.send(report);

    const [address, request] = fetchMock.mock.calls[0];
    expect(address).toBe('/feedback/reports');
    expect(request.method).toBe('POST');
    expect(request.headers.Authorization).toBe('Bearer jwt-123');
    expect(request.headers['Content-Type']).toBe('application/json');
    expect(JSON.parse(request.body)).toEqual(report);
    expect(filed).toEqual({ reference: 7400 });
  });

  it('posts to the relay a deployment names instead', async () => {
    (import.meta.env as Record<string, string>).VITE_FEEDBACK_URL = 'https://feedback.example.org/';
    fetchMock.mockResolvedValue(answer(200, { reference: 1 }));

    await feedbackApi.send(report);

    expect(fetchMock.mock.calls[0][0]).toBe('https://feedback.example.org/reports');
  });

  it('turns a refusal into the relay’s code and the values its words need', async () => {
    fetchMock.mockResolvedValue(answer(429, { code: 'tooManyRequests', error: 'Too many', values: { seconds: 40 } }));

    const refused = await feedbackApi.send(report).catch((error: unknown) => error);

    expect(refused).toBeInstanceOf(FeedbackRefusedError);
    expect((refused as FeedbackRefusedError).code).toBe('tooManyRequests');
    expect((refused as FeedbackRefusedError).values).toEqual({ seconds: 40 });
  });

  it('refuses without a code when the answer is not the relay’s', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 502, json: () => Promise.reject(new SyntaxError('not JSON')) });

    const refused = await feedbackApi.send(report).catch((error: unknown) => error);

    expect(refused).toBeInstanceOf(FeedbackRefusedError);
    expect((refused as FeedbackRefusedError).code).toBe('');
  });

  it('is offered unless a deployment switches it off', () => {
    expect(feedbackApi.offered()).toBe(true);
    (import.meta.env as Record<string, string>).VITE_FEEDBACK_URL = 'off';
    expect(feedbackApi.offered()).toBe(false);
  });
});
