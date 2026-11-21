/**
 * A submitter reads their findings through the exchange a submission uses: a code sent to the address,
 * spent on a short-lived ticket, spent on one request. It proves this mailbox and the submission naming it,
 * because a reference alone is known to whoever submitted and to anyone who guessed it.
 */
import { intakeApi, intakeRequest, renewedTicket } from './intakeApi';
import type { FindingsAnswer } from '../publicPages/answeredFindings';
import type { TemporalReduceQuery, TemporalReduceResponse } from '@tendril-ui/tendril';

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
};
