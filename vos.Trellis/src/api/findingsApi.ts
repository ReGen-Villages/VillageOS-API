/**
 * A submitter reads their findings through the exchange a submission uses: a code sent to the address,
 * spent on a short-lived ticket, spent on one request. It proves this mailbox and the submission naming it,
 * because a reference alone is known to whoever submitted and to anyone who guessed it.
 */
import { intakeApi, intakeRequest, intakeServiceAddress, renewedTicket, TICKET_HEADER } from './intakeApi';
import { refusalIn } from './refusals';
import type { SharedSurvey } from '../explore/sharedSurveys';
import type { FindingsAnswer } from '../publicFindings/answeredFindings';
import type { TemporalReduceQuery, TemporalReduceResponse } from '@tendril-ui/tendril';
import i18n from '../i18n';

/** Without the Thing, which the service supplies as the submission's own site. */
type FindingsReduceQuery = Omit<TemporalReduceQuery, 'thingId'>;

export const findingsApi = {
  /** The same route and limit as a submission's verification. */
  askForCode: intakeApi.askForCode,

  read: async (
    submissionId: string, emailAddress: string, code: string,
  ): Promise<{ findings: FindingsAnswer; ticket: string }> => {
    const ticket = await intakeApi.exchangeTicket(emailAddress, code);
    return findingsApi.readWithTicket(submissionId, emailAddress, ticket);
  },

  /** Under the ticket the submission was posted with, so a page that just submitted opens its findings with
   *  nothing typed again. Each accepted read answers with a fresh ticket, which the caller uses next. */
  readWithTicket: async (
    submissionId: string,
    emailAddress: string,
    ticket: string,
  ): Promise<{ findings: FindingsAnswer; ticket: string }> => {
    const response = await intakeRequest('/submissions/findings', { body: { submissionId, emailAddress }, ticket });
    return { findings: await response.json(), ticket: renewedTicket(response, ticket) };
  },

  /** The service passes the question on under the findings' ticket and scopes it to the submission's site,
   *  so the page names no Thing. A fresh ticket comes back, as on every read. */
  reduceWithTicket: async (
    submissionId: string,
    ticket: string,
    question: FindingsReduceQuery,
  ): Promise<{ answer: TemporalReduceResponse; ticket: string }> => {
    const response = await intakeRequest(`/findings/${submissionId}/reduce`, { body: question, ticket });
    return { answer: await response.json(), ticket: renewedTicket(response, ticket) };
  },

  /** Sent with XMLHttpRequest rather than `fetch`, because only it reports upload progress, and a large file
   *  from a phone takes long enough to want it. */
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
          reject(refusalIn(parsedOrNull(request.responseText), request.status));
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
    const response = await intakeRequest(`/submissions/${submissionId}/documents`, { ticket });
    const answered = (await response.json()) as { documents?: SharedSurvey[] };
    return { documents: answered.documents ?? [], ticket: renewedTicket(response, ticket) };
  },
};

function parsedOrNull(text: string): { error?: string } | null {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}
