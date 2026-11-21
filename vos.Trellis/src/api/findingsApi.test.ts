import { describe, it, expect, vi, beforeEach } from 'vitest';

import { findingsApi } from './findingsApi';

const fetchMock = vi.fn();

const REFERENCE = '9f1c74d6-0b8e-4a52-bd31-6c7e5a92f048';
const ADDRESS = 'ana.ferreira@example.pt';

const ANSWER = { specification: '{}', scopeId: 'site-1', things: [], relationships: [], ranges: {} };

/** Every faked answer carries headers, because a real accepted read returns the renewed ticket in one. */
function answering(findingsAnswer: object) {
  fetchMock.mockImplementation((url: string) =>
    Promise.resolve(
      url.endsWith('/submissions/ticket')
        ? { ok: true, headers: new Headers(), json: () => Promise.resolve({ ticket: 'ticket-1', validForSeconds: 600 }) }
        : { headers: new Headers(), ...findingsAnswer },
    ),
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  (import.meta.env as Record<string, string>).VITE_INTAKE_URL = 'http://localhost:6200';
  vi.stubGlobal('fetch', fetchMock);
});

describe('asking the intake service for one submission\'s findings', () => {
  // In one call, because a ticket lasts minutes and is no use beyond the request it was got for.
  it('spends the code on a ticket and the ticket on the read', async () => {
    answering({ ok: true, json: () => Promise.resolve(ANSWER) });

    await findingsApi.read(REFERENCE, ADDRESS, '123456');

    expect(fetchMock).toHaveBeenNthCalledWith(
      1,
      'http://localhost:6200/submissions/ticket',
      expect.objectContaining({ body: JSON.stringify({ emailAddress: ADDRESS, code: '123456' }) }),
    );
    expect(fetchMock).toHaveBeenNthCalledWith(
      2,
      'http://localhost:6200/submissions/findings',
      expect.objectContaining({
        headers: expect.objectContaining({ 'X-Submission-Ticket': 'ticket-1' }),
        body: JSON.stringify({ submissionId: REFERENCE, emailAddress: ADDRESS }),
      }),
    );
  });

  it('answers with what the service read, and the ticket the page carries on with', async () => {
    answering({ ok: true, headers: new Headers({ 'X-Submission-Ticket': 'ticket-2' }), json: () => Promise.resolve(ANSWER) });

    await expect(findingsApi.read(REFERENCE, ADDRESS, '123456')).resolves.toEqual({ findings: ANSWER, ticket: 'ticket-2' });
  });

  // The service's words, not the status, because they say whether the reference or the address was wrong.
  it('carries the service\'s own refusal', async () => {
    answering({
      ok: false,
      status: 404,
      json: () => Promise.resolve({ error: 'No submission was found for that reference and address.' }),
    });

    await expect(findingsApi.read(REFERENCE, ADDRESS, '123456')).rejects.toThrow(
      'No submission was found for that reference and address.',
    );
  });

  it('does not ask for findings when the code bought no ticket', async () => {
    fetchMock.mockResolvedValue({ ok: false, status: 403, json: () => Promise.resolve({ error: 'Not the code.' }) });

    await expect(findingsApi.read(REFERENCE, ADDRESS, '000000')).rejects.toThrow('Not the code.');
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });

  it('refuses to ask a service whose address is not configured', async () => {
    (import.meta.env as Record<string, string>).VITE_INTAKE_URL = '';

    await expect(findingsApi.read(REFERENCE, ADDRESS, '123456')).rejects.toThrow(/VITE_INTAKE_URL/);
  });
});

describe('asking the intake service to reduce the site\'s history', () => {
  it('posts the question under the ticket to the submission\'s own route, and carries the renewed ticket back', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      headers: new Headers({ 'X-Submission-Ticket': 'ticket-2' }),
      json: () => Promise.resolve({ Groups: [{ Key: '1', Value: 27.4 }], Samples: 8760, UnusableSamples: 0 }),
    });
    const question = {
      property: 'temperature', windowSeconds: 31_536_000, steps: [{ fold: 'monthOfYear' as const, function: 'Max' as const }],
    };

    const answered = await findingsApi.reduceWithTicket(REFERENCE, 'ticket-1', question);

    expect(fetchMock).toHaveBeenCalledWith(
      `http://localhost:6200/findings/${REFERENCE}/reduce`,
      expect.objectContaining({
        method: 'POST',
        headers: expect.objectContaining({ 'X-Submission-Ticket': 'ticket-1' }),
        body: JSON.stringify(question),
      }),
    );
    expect(answered).toEqual({
      answer: { Groups: [{ Key: '1', Value: 27.4 }], Samples: 8760, UnusableSamples: 0 },
      ticket: 'ticket-2',
    });
  });

  it('raises what the service said when it refuses the question', async () => {
    fetchMock.mockResolvedValue({
      ok: false, status: 400, headers: new Headers(),
      json: () => Promise.resolve({ error: 'A fold of hour over eleven years exceeds ten thousand groups.' }),
    });

    await expect(findingsApi.reduceWithTicket(REFERENCE, 'ticket-1', { property: 'temperature', windowSeconds: 1, steps: [] }))
      .rejects.toThrow('ten thousand groups');
  });
});
