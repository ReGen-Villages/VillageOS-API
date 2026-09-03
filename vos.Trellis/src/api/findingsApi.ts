/**
 * What the intake service answers a submitter who asks about their own land.
 *
 * The exchange is the one a submission already goes through: a code to the address, the code spent on a
 * short-lived ticket, the ticket spent on the one request it was got for. What it establishes here is
 * the same pair the service checks — this mailbox, and the submission that names it — because a
 * reference is known to whoever submitted and to anybody who guessed one.
 */
import { intakeApi, intakeServiceAddress, refusalFrom, TICKET_HEADER } from './intakeApi';
import type { FindingsAnswer } from '../publicFindings/answeredFindings';

export const findingsApi = {
  /** Asks the service to send a code to the address, which is the step that establishes somebody reads
   *  what is sent there. The same route a submission's verification uses, and the same budget. */
  askForCode: intakeApi.askForCode,

  read: async (submissionId: string, emailAddress: string, code: string): Promise<FindingsAnswer> => {
    const ticket = await intakeApi.exchangeTicket(emailAddress, code);
    return (await findingsApi.readWithTicket(submissionId, emailAddress, ticket)).findings;
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
};
