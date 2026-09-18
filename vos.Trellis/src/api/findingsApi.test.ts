import { describe, it, expect, vi, beforeEach } from 'vitest';

import { findingsApi } from './findingsApi';

const fetchMock = vi.fn();

const REFERENCE = '9f1c74d6-0b8e-4a52-bd31-6c7e5a92f048';
const ADDRESS = 'ana.ferreira@example.pt';

const ANSWER = { spec: '{}', scopeId: 'site-1', things: [], relationships: [], ranges: {} };

/** The ticket exchange first, then whatever the test says the findings request is answered with. Every
 *  faked answer carries a header map because every real one does — an accepted read hands the renewed
 *  ticket back on it. */
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

  it('answers with what the service read, and the ticket the page carries on with', async () => {
    answering({ ok: true, headers: new Headers({ 'X-Submission-Ticket': 'ticket-2' }), json: () => Promise.resolve(ANSWER) });

    await expect(findingsApi.read(REFERENCE, ADDRESS, '123456')).resolves.toEqual({ findings: ANSWER, ticket: 'ticket-2' });
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

// The history reduction a chart asks for after the findings arrived: the same ticket, the same renewal
// on the way back, and the site supplied by the service rather than named by the page.
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

// A survey a submitter shares after the report: the bytes go up as a form under the ticket, the browser
// reports how far they have got, and the service answers the document the model now holds.
describe('sharing a survey file through the intake service', () => {
  /** Stands in for the browser's own request object, which is the one that reports upload progress —
   *  `fetch` has no way to. What the page hands it and what it fires back are both scripted here. */
  class ScriptedRequest {
    static opened: { method: string; url: string }[] = [];
    static headers: Record<string, string> = {};
    static sent: FormData | null = null;
    static answer = { status: 201, body: '', ticket: null as string | null };
    static progress: { loaded: number; total: number }[] = [];

    upload = { onprogress: null as ((event: { lengthComputable: boolean; loaded: number; total: number }) => void) | null };
    onload: (() => void) | null = null;
    onerror: (() => void) | null = null;
    status = 0;
    responseText = '';

    open(method: string, url: string) { ScriptedRequest.opened.push({ method, url }); }
    setRequestHeader(name: string, value: string) { ScriptedRequest.headers[name] = value; }
    getResponseHeader(name: string) { return name === 'X-Submission-Ticket' ? ScriptedRequest.answer.ticket : null; }
    send(body: FormData) {
      ScriptedRequest.sent = body;
      for (const step of ScriptedRequest.progress) this.upload.onprogress?.({ lengthComputable: true, ...step });
      this.status = ScriptedRequest.answer.status;
      this.responseText = ScriptedRequest.answer.body;
      this.onload?.();
    }
  }

  const DOCUMENT = {
    id: 'doc-1', fileName: 'soil.pdf', description: 'The soil survey', contentType: 'application/pdf',
    sizeBytes: 4, sharedAt: '2026-09-18T10:00:00.0000000Z',
  };

  beforeEach(() => {
    ScriptedRequest.opened = [];
    ScriptedRequest.headers = {};
    ScriptedRequest.sent = null;
    ScriptedRequest.progress = [];
    ScriptedRequest.answer = { status: 201, body: JSON.stringify(DOCUMENT), ticket: 'ticket-2' };
    vi.stubGlobal('XMLHttpRequest', ScriptedRequest);
  });

  it('posts the file and its description as a form under the ticket, reporting progress, and carries the renewed ticket back', async () => {
    ScriptedRequest.progress = [{ loaded: 2, total: 4 }, { loaded: 4, total: 4 }];
    const reported: number[] = [];
    const file = new File(['soil'], 'soil.pdf', { type: 'application/pdf' });

    const shared = await findingsApi.shareDocumentWithTicket(REFERENCE, 'ticket-1', file, 'The soil survey', (fraction) => reported.push(fraction));

    expect(ScriptedRequest.opened).toEqual([{ method: 'POST', url: `http://localhost:6200/submissions/${REFERENCE}/documents` }]);
    expect(ScriptedRequest.headers).toEqual({ 'X-Submission-Ticket': 'ticket-1' });
    expect(ScriptedRequest.sent!.get('file')).toBe(file);
    expect(ScriptedRequest.sent!.get('description')).toBe('The soil survey');
    expect(reported).toEqual([0.5, 1]);
    expect(shared).toEqual({ document: DOCUMENT, ticket: 'ticket-2' });
  });

  it('carries the service\'s own refusal', async () => {
    ScriptedRequest.answer = { status: 413, body: JSON.stringify({ error: 'A file may be at most 25 MB.' }), ticket: null };

    await expect(findingsApi.shareDocumentWithTicket(REFERENCE, 'ticket-1', new File(['x'], 'x.bin'), '', () => undefined))
      .rejects.toThrow('A file may be at most 25 MB.');
  });

  it('lists the files the model holds for the submission under the ticket', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      headers: new Headers({ 'X-Submission-Ticket': 'ticket-2' }),
      json: () => Promise.resolve({ documents: [DOCUMENT] }),
    });

    const listed = await findingsApi.listDocumentsWithTicket(REFERENCE, 'ticket-1');

    expect(fetchMock).toHaveBeenCalledWith(
      `http://localhost:6200/submissions/${REFERENCE}/documents`,
      expect.objectContaining({ method: 'GET', headers: expect.objectContaining({ 'X-Submission-Ticket': 'ticket-1' }) }),
    );
    expect(listed).toEqual({ documents: [DOCUMENT], ticket: 'ticket-2' });
  });
});
