import { describe, it, expect, vi, beforeEach } from 'vitest';

import { findingsApi } from './findingsApi';

const fetchMock = vi.fn();

const REFERENCE = '9f1c74d6-0b8e-4a52-bd31-6c7e5a92f048';
const ADDRESS = 'ana.ferreira@example.pt';

const ANSWER = { spec: '{}', scopeId: 'site-1', things: [], relationships: [], ranges: {} };

/** The ticket exchange first, then whatever the test says the findings request is answered with. */
function answering(findingsAnswer: unknown) {
  fetchMock.mockImplementation((url: string) =>
    Promise.resolve(
      url.endsWith('/submissions/ticket')
        ? { ok: true, json: () => Promise.resolve({ ticket: 'ticket-1', validForSeconds: 600 }) }
        : findingsAnswer,
    ),
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  (import.meta.env as Record<string, string>).VITE_INTAKE_URL = 'http://localhost:6200';
  vi.stubGlobal('fetch', fetchMock);
});

describe('asking the intake service for one submission\'s findings', () => {
  // The code is spent on a ticket and the ticket on the read, in one act: a ticket lasts minutes and is
  // no use to the page beyond the request it was got for.
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

  it('answers with what the service read', async () => {
    answering({ ok: true, json: () => Promise.resolve(ANSWER) });

    await expect(findingsApi.read(REFERENCE, ADDRESS, '123456')).resolves.toEqual(ANSWER);
  });

  // What the service said, not the status it said it under: which of the reference and the address was
  // wrong is the whole value of the answer to whoever is reading it.
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

  // A code that was not the one sent buys no ticket, and the read is never attempted.
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
