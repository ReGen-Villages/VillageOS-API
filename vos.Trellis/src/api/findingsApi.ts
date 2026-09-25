/**
 * What the intake service answers a submitter who asks about their own land.
 *
 * The exchange is the one a submission already goes through: a code to the address, the code spent on a
 * short-lived ticket, the ticket spent on the one request it was got for. What it establishes here is
 * the same pair the service checks — this mailbox, and the submission that names it — because a
 * reference is known to whoever submitted and to anybody who guessed one.
 */
import { intakeApi, intakeServiceAddress, refusalFrom, refusalIn, TICKET_HEADER } from './intakeApi';
import type { SharedSurvey } from '../explore/sharedSurveys';
import type { FindingsAnswer } from '../publicFindings/answeredFindings';
import type { TemporalReduceQuery, TemporalReduceResponse } from '../types/vos';
import i18n from '../i18n';

/** The history reduction as the page asks it: the platform's question without the Thing, which the
 *  service supplies as the submission's own site. */
export type FindingsReduceQuery = Omit<TemporalReduceQuery, 'thingId'>;

export const findingsApi = {
  /** Asks the service to send a code to the address, which is the step that establishes somebody reads
   *  what is sent there. The same route a submission's verification uses, and the same budget. */
  askForCode: intakeApi.askForCode,

  read: async (
    submissionId: string, emailAddress: string, code: string,
  ): Promise<{ findings: FindingsAnswer; ticket: string }> => {
    const ticket = await intakeApi.exchangeTicket(emailAddress, code);
    return findingsApi.readWithTicket(submissionId, emailAddress, ticket);
  },

  /** Reads under a ticket already held — the one the submission itself was posted with, so a page that
   *  just submitted lands on its findings with nothing retyped. Every accepted read hands a fresh
   *  ticket back, and the caller carries on with whichever came back. */
  readWithTicket: async (
    submissionId: string,
    emailAddress: string,
    ticket: string,
  ): Promise<{ findings: FindingsAnswer; ticket: string }> => {
    const response = await fetch(`${intakeServiceAddress()}/submissions/findings`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', [TICKET_HEADER]: ticket },
      body: JSON.stringify({ submissionId, emailAddress }),
    });

    if (!response.ok) throw new Error(await refusalFrom(response));
    return {
      findings: await response.json(),
      ticket: response.headers.get(TICKET_HEADER) ?? ticket,
    };
  },

  /** One property's history on the submission's site, reduced by the platform and proxied by the
   *  service under the ticket the findings were read with. The service scopes the question to that
   *  submission's site, so the page names no Thing. A fresh ticket rides back as on every read. */
  reduceWithTicket: async (
    submissionId: string,
    ticket: string,
    question: FindingsReduceQuery,
  ): Promise<{ answer: TemporalReduceResponse; ticket: string }> => {
    const response = await fetch(`${intakeServiceAddress()}/findings/${submissionId}/reduce`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', [TICKET_HEADER]: ticket },
      body: JSON.stringify(question),
    });

    if (!response.ok) throw new Error(await refusalFrom(response));
    return {
      answer: await response.json(),
      ticket: response.headers.get(TICKET_HEADER) ?? ticket,
    };
  },

  /** A survey shared about the land: the file and its description go up as a form under the ticket and
   *  the service answers the document the model now holds. Sent through the browser's own request
   *  object rather than `fetch`, which is the one of the two that reports how far the bytes have got —
   *  a 25 MB file on a phone is long enough to want telling. */
  shareDocumentWithTicket: (
    submissionId: string,
    ticket: string,
    file: File,
    description: string,
    onProgress: (fraction: number) => void,
  ): Promise<{ document: SharedSurvey; ticket: string }> =>
    new Promise((resolve, reject) => {
      const request = new XMLHttpRequest();
      request.open('POST', `${intakeServiceAddress()}/submissions/${submissionId}/documents`);
      request.setRequestHeader(TICKET_HEADER, ticket);
      request.upload.onprogress = (event) => {
        if (event.lengthComputable && event.total > 0) onProgress(event.loaded / event.total);
      };
      request.onerror = () => reject(new Error(i18n.t('publicFindings.fileNotSent')));
      request.onload = () => {
        if (request.status < 200 || request.status >= 300) {
          reject(new Error(refusalIn(parsedOrNull(request.responseText), request.status)));
          return;
        }
        resolve({
          document: JSON.parse(request.responseText) as SharedSurvey,
          ticket: request.getResponseHeader(TICKET_HEADER) ?? ticket,
        });
      };
      const form = new FormData();
      form.append('file', file);
      form.append('description', description);
      request.send(form);
    }),

  listDocumentsWithTicket: async (
    submissionId: string,
    ticket: string,
  ): Promise<{ documents: SharedSurvey[]; ticket: string }> => {
    const response = await fetch(`${intakeServiceAddress()}/submissions/${submissionId}/documents`, {
      method: 'GET',
      headers: { [TICKET_HEADER]: ticket },
    });

    if (!response.ok) throw new Error(await refusalFrom(response));
    const answered = (await response.json()) as { documents?: SharedSurvey[] };
    return {
      documents: answered.documents ?? [],
      ticket: response.headers.get(TICKET_HEADER) ?? ticket,
    };
  },
};

function parsedOrNull(text: string): { error?: string } | null {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}
