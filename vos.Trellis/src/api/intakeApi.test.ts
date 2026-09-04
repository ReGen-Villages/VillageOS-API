import { describe, it, expect, vi, beforeEach } from 'vitest';

import { intakeApi } from './intakeApi';
import type { SubmissionDocument } from '../intake/submissionDraft';

const fetchMock = vi.fn();

const SUBMISSION: SubmissionDocument = {
  submissionId: '9f1c74d6-0b8e-4a52-bd31-6c7e5a92f048',
  project: { name: 'Willow Bend Regeneration' },
  contact: { name: 'Ana Ferreira', emailAddress: 'ana.ferreira@example.pt' },
  site: { name: 'Willow Bend', latitude: 39.5012, longitude: -8.4137 },
};

/** The ticket exchange first, then whatever the test says the submission is answered with. Every faked
 *  answer carries a header map because every real one does — the accepting route reads the renewed
 *  ticket off it. */
function answering(submissionAnswer: object) {
  fetchMock.mockImplementation((url: string) =>
    Promise.resolve(
      url.endsWith('/submissions/ticket')
        ? { ok: true, headers: new Headers(), json: () => Promise.resolve({ ticket: 'ticket-1', validForSeconds: 600 }) }
        : { headers: new Headers(), ...submissionAnswer },
    ),
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  (import.meta.env as Record<string, string>).VITE_INTAKE_URL = 'http://localhost:6200';
  vi.stubGlobal('fetch', fetchMock);
});

describe('posting a submission', () => {
  it('spends the code on a ticket and posts the document under it', async () => {
    answering({ ok: true, json: () => Promise.resolve({ reference: 'sub-ref-1' }) });

    const accepted = await intakeApi.submit(SUBMISSION, '314159');

    const [ticketUrl, ticketInit] = fetchMock.mock.calls[0];
    expect(ticketUrl).toBe('http://localhost:6200/submissions/ticket');
    expect(JSON.parse(ticketInit.body)).toEqual({
      emailAddress: 'ana.ferreira@example.pt',
      code: '314159',
    });
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

    await intakeApi.submit(SUBMISSION, '314159');

    expect(fetchMock.mock.calls[1][1].headers.Authorization).toBeUndefined();
  });

  it('does not double the separator when the configured address ends in one', async () => {
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = 'http://localhost:6200/';
    answering({ ok: true, json: () => Promise.resolve({ reference: 'sub-ref-1' }) });

    await intakeApi.submit(SUBMISSION, '314159');

    expect(fetchMock.mock.calls[0][0]).toBe('http://localhost:6200/submissions/ticket');
    expect(fetchMock.mock.calls[1][0]).toBe('http://localhost:6200/submissions');
  });

  it('raises what the service said was wrong, not the status it said it under', async () => {
    answering({
      ok: false,
      status: 400,
      json: () => Promise.resolve({ error: "'site.name' is missing: a Thing is created under a name." }),
    });

    await expect(intakeApi.submit(SUBMISSION, '314159')).rejects.toThrow("'site.name' is missing");
  });

  it('raises the detail where the service answered with a problem document instead', async () => {
    answering({
      ok: false,
      status: 503,
      json: () => Promise.resolve({ title: 'Service unavailable', detail: 'This service cannot accept submissions at the moment.' }),
    });

    await expect(intakeApi.submit(SUBMISSION, '314159')).rejects.toThrow('cannot accept submissions');
  });

  it('still says the submission was refused when the refusal carries no readable body', async () => {
    answering({ ok: false, status: 503, json: () => Promise.reject(new Error('no body')) });

    await expect(intakeApi.submit(SUBMISSION, '314159')).rejects.toThrow('503');
  });

  it('raises the refusal where the ticket itself could not be had', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 429,
      json: () => Promise.resolve({ error: 'Too many requests. Try again in 600 seconds.' }),
    });

    await expect(intakeApi.submit(SUBMISSION, '314159')).rejects.toThrow('Too many requests');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('refuses to post at all when no intake address is configured', async () => {
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = '';

    await expect(intakeApi.submit(SUBMISSION, '314159')).rejects.toThrow('VITE_INTAKE_URL');
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('reports whether an address is configured, which decides whether the wizard is offered', () => {
    expect(intakeApi.configured()).toBe(true);
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = '';
    expect(intakeApi.configured()).toBe(false);
  });
});

describe('asking for a code', () => {
  it('names the address the code is to be sent to', async () => {
    fetchMock.mockResolvedValue({ ok: true, json: () => Promise.resolve({}) });

    await intakeApi.askForCode('ana.ferreira@example.pt');

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:6200/submissions/verification');
    expect(init.method).toBe('POST');
    expect(JSON.parse(init.body)).toEqual({ emailAddress: 'ana.ferreira@example.pt' });
    expect(init.headers.Authorization).toBeUndefined();
  });

  it('raises what the service said was wrong', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 429,
      json: () => Promise.resolve({ error: 'That address has been sent as many codes as it can be for now.' }),
    });

    await expect(intakeApi.askForCode('ana.ferreira@example.pt')).rejects.toThrow('as many codes');
  });
});

describe('what the form draws itself with', () => {
  it('asks the service, because a page holding no credential cannot read the model', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: () => Promise.resolve({ allocationCategories: ['residential'], basemapSources: [] }),
    });

    const options = await intakeApi.formOptions();

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('http://localhost:6200/submissions/form');
    expect(init).toBeUndefined();
    expect(options.allocationCategories).toEqual(['residential']);
  });

  it('judges the sources it is told about by the rule the signed-in page judges them by', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      json: () =>
        Promise.resolve({
          allocationCategories: [],
          basemapSources: [
            { id: 'src-1', name: 'Streets', attribution: 'Example', styleUrl: 'https://example.test/s.json' },
            { id: 'src-2', name: 'Unlicensed', attribution: null, styleUrl: 'https://example.test/u.json' },
          ],
        }),
    });

    const options = await intakeApi.formOptions();

    expect(options.basemapSources.map((source) => source.name)).toEqual(['Streets']);
  });

  // A deployment whose model is not seeded answers this route with a problem document, and a form that
  // read the absent fields as an empty model would offer a programme step with nothing in it and no
  // explanation.
  it('raises what the service said rather than reading a refusal as an empty model', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 503,
      json: () => Promise.resolve({ detail: 'This service cannot answer at the moment.' }),
    });

    await expect(intakeApi.formOptions()).rejects.toThrow('cannot answer');
  });
});
