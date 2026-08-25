import type { SubmissionDocument } from '../pages/intakeWizard';

/** What the intake service answered a submission with: something the submitter can quote to whoever
 *  reviews it. What the model called the Things it composed the submission into stays inside the service —
 *  the route is anonymous, and a stranger is not told the shape of the model they submitted into. */
export interface SubmissionAccepted {
  reference: string;
}

/**
 * The intake service is a program of its own rather than a service reached through the broker's endpoint
 * forward, and deliberately so: that route resolves where to forward from data in the model, so anything
 * the model named would be within reach of whoever could call it. Intake holds its own credential and its
 * own address, which is what lets it take a submission from someone who holds none.
 *
 * Its address is configuration for the same reason the ingestion service's is — one deployment may run
 * it beside the broker and another somewhere else entirely.
 */
const intakeUrl = () => (import.meta.env.VITE_INTAKE_URL as string | undefined) || '';

/** The service refuses a submission that did not ask for one of these first, which is what a submitter
 *  posting blind at the address never does. */
const TICKET_HEADER = 'X-Submission-Ticket';

export const intakeApi = {
  /** Whether an intake address is configured, which is what decides whether the wizard is offered at
   *  all: a wizard that collects a submission it cannot post is worse than no wizard. */
  configured: () => intakeUrl().length > 0,

  submit: async (submission: SubmissionDocument): Promise<SubmissionAccepted> => {
    const base = intakeUrl().replace(/\/$/, '');
    if (!base) throw new Error('The intake service address is not configured (set VITE_INTAKE_URL).');

    const ticket = await fetch(`${base}/submissions/ticket`);
    if (!ticket.ok) throw new Error(await refusalFrom(ticket));

    const response = await fetch(`${base}/submissions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', [TICKET_HEADER]: (await ticket.json()).ticket },
      body: JSON.stringify(submission),
    });

    if (!response.ok) throw new Error(await refusalFrom(response));
    return response.json();
  },
};

/** What the service said, rather than the status code it said it under. A refused submission names the
 *  field to correct, and that is the whole value of the answer to whoever filled the form in. */
async function refusalFrom(response: Response): Promise<string> {
  try {
    const body = await response.json();
    return body?.error ?? body?.detail ?? body?.title ?? `The submission was refused (${response.status}).`;
  } catch {
    return `The submission was refused (${response.status}).`;
  }
}
