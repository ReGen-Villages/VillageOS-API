import { describe, it, expect, vi, beforeEach } from 'vitest';

import { intakeApi } from './intakeApi';
import type { SubmissionDocument } from '../pages/intakeWizard';

const fetchMock = vi.fn();

const SUBMISSION: SubmissionDocument = {
  submissionId: '9f1c74d6-0b8e-4a52-bd31-6c7e5a92f048',
  site: { name: 'Willow Bend', latitude: 39.5012, longitude: -8.4137 },
};

/** The ticket exchange first, then whatever the test says the submission is answered with. */
function answering(submissionAnswer: unknown) {
  fetchMock.mockImplementation((url: string) =>
    Promise.resolve(
      url.endsWith('/submissions/ticket')
        ? { ok: true, json: () => Promise.resolve({ ticket: 'ticket-1', validForSeconds: 600 }) }
        : submissionAnswer,
    ),
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  (import.meta.env as Record<string, string>).VITE_INTAKE_URL = 'http://localhost:6200';
  vi.stubGlobal('fetch', fetchMock);
});

describe('posting a submission', () => {
  it('asks for a ticket and posts the document under it', async () => {
    answering({ ok: true, json: () => Promise.resolve({ reference: 'sub-ref-1' }) });

    const accepted = await intakeApi.submit(SUBMISSION);

    expect(fetchMock.mock.calls[0][0]).toBe('http://localhost:6200/submissions/ticket');
    const [url, init] = fetchMock.mock.calls[1];
    expect(url).toBe('http://localhost:6200/submissions');
    expect(init.method).toBe('POST');
    expect(init.headers['X-Submission-Ticket']).toBe('ticket-1');
    expect(init.headers['Content-Type']).toBe('application/json');
    expect(JSON.parse(init.body)).toEqual(SUBMISSION);
    expect(accepted.reference).toBe('sub-ref-1');
  });

  // The service holds its own credential and decides for itself who may submit. A form sent under a
  // planner's token would work only for a planner, and this is the route a stranger uses.
  it('carries no credential', async () => {
    answering({ ok: true, json: () => Promise.resolve({ reference: 'sub-ref-1' }) });

    await intakeApi.submit(SUBMISSION);

    expect(fetchMock.mock.calls[1][1].headers.Authorization).toBeUndefined();
  });

  it('does not double the separator when the configured address ends in one', async () => {
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = 'http://localhost:6200/';
    answering({ ok: true, json: () => Promise.resolve({ reference: 'sub-ref-1' }) });

    await intakeApi.submit(SUBMISSION);

    expect(fetchMock.mock.calls[0][0]).toBe('http://localhost:6200/submissions/ticket');
    expect(fetchMock.mock.calls[1][0]).toBe('http://localhost:6200/submissions');
  });

  it('raises what the service said was wrong, not the status it said it under', async () => {
    answering({
      ok: false,
      status: 400,
      json: () => Promise.resolve({ error: "'site.name' is missing: a Thing is created under a name." }),
    });

    await expect(intakeApi.submit(SUBMISSION)).rejects.toThrow("'site.name' is missing");
  });

  it('raises the detail where the service answered with a problem document instead', async () => {
    answering({
      ok: false,
      status: 503,
      json: () => Promise.resolve({ title: 'Service unavailable', detail: 'This service cannot accept submissions at the moment.' }),
    });

    await expect(intakeApi.submit(SUBMISSION)).rejects.toThrow('cannot accept submissions');
  });

  it('still says the submission was refused when the refusal carries no readable body', async () => {
    answering({ ok: false, status: 503, json: () => Promise.reject(new Error('no body')) });

    await expect(intakeApi.submit(SUBMISSION)).rejects.toThrow('503');
  });

  it('raises the refusal where the ticket itself could not be had', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 429,
      json: () => Promise.resolve({ error: 'Too many requests. Try again in 600 seconds.' }),
    });

    await expect(intakeApi.submit(SUBMISSION)).rejects.toThrow('Too many requests');
    expect(fetchMock).toHaveBeenCalledTimes(1);
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
