import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./client', () => ({ apiClient: { ensureToken: vi.fn() } }));

import { intakeApi } from './intakeApi';
import { apiClient } from './client';
import type { SubmissionDocument } from '../pages/intakeWizard';

const fetchMock = vi.fn();

const SUBMISSION: SubmissionDocument = {
  submissionId: 'sub-0001',
  site: { name: 'Willow Bend', latitude: 39.5012, longitude: -8.4137 },
};

beforeEach(() => {
  vi.clearAllMocks();
  (import.meta.env as Record<string, string>).VITE_INTAKE_URL = 'http://localhost:6200';
  vi.stubGlobal('fetch', fetchMock);
  vi.mocked(apiClient.ensureToken).mockResolvedValue('jwt-123');
});

describe('posting a submission', () => {
  it('posts the document to <intake>/submissions under a bearer token', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ siteId: 'site-1', studyId: 'study-1', parcelId: null }),
    });

    const accepted = await intakeApi.submit(SUBMISSION);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:6200/submissions');
    expect(init.method).toBe('POST');
    expect(init.headers.Authorization).toBe('Bearer jwt-123');
    expect(init.headers['Content-Type']).toBe('application/json');
    expect(JSON.parse(init.body)).toEqual(SUBMISSION);
    expect(accepted.siteId).toBe('site-1');
  });

  it('does not double the separator when the configured address ends in one', async () => {
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = 'http://localhost:6200/';
    fetchMock.mockResolvedValue({ ok: true, json: () => Promise.resolve({ siteId: 's', studyId: 't' }) });

    await intakeApi.submit(SUBMISSION);

    expect(fetchMock.mock.calls[0][0]).toBe('http://localhost:6200/submissions');
  });

  it('raises what the service said was wrong, not the status it said it under', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 400,
      json: () => Promise.resolve({ error: "'site.name' is missing: a Thing is created under a name." }),
    });

    await expect(intakeApi.submit(SUBMISSION)).rejects.toThrow("'site.name' is missing");
  });

  it('raises the detail where the service answered with a problem document instead', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 503,
      json: () => Promise.resolve({ title: 'Service unavailable', detail: 'This service cannot accept submissions at the moment.' }),
    });

    await expect(intakeApi.submit(SUBMISSION)).rejects.toThrow('cannot accept submissions');
  });

  it('still says the submission was refused when the refusal carries no readable body', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 503, json: () => Promise.reject(new Error('no body')) });

    await expect(intakeApi.submit(SUBMISSION)).rejects.toThrow('503');
  });

  it('refuses to post at all when no intake address is configured', async () => {
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = '';

    await expect(intakeApi.submit(SUBMISSION)).rejects.toThrow('VITE_INTAKE_URL');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('reports whether an address is configured, which decides whether the wizard is offered', () => {
    expect(intakeApi.configured()).toBe(true);
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = '';
    expect(intakeApi.configured()).toBe(false);
  });
});
