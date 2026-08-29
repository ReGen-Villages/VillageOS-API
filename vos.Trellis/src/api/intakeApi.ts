import type { SubmissionDocument } from '../intake/submissionDraft';
import type { BasemapSource, DeclaredBasemapSource } from '../types/basemap';
import { basemapSourcesFrom } from './basemapApi';

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

/** The service issues one of these in exchange for a code it sent, and reads it back when the form is
 *  posted. It is signed against the address it was issued for, so a submission naming another does not
 *  carry a ticket for itself. */
const TICKET_HEADER = 'X-Submission-Ticket';

/** What the form draws itself with. A page holding no credential cannot read the model for itself, so
 *  the service reads it and answers with the categories a submission may name and the imagery a map may
 *  draw on. What makes a source usable is judged here, by the same rule the planner's page judges it by. */
export interface FormOptions {
  allocationCategories: string[];
  basemapSources: BasemapSource[];
}

export const intakeApi = {
  /** Whether an intake address is configured, which is what decides whether the wizard is offered at
   *  all: a wizard that collects a submission it cannot post is worse than no wizard. */
  configured: () => intakeUrl().length > 0,

  formOptions: async (): Promise<FormOptions> => {
    const response = await fetch(`${serviceAddress()}/submissions/form`);
    if (!response.ok) throw new Error(await refusalFrom(response));

    const answered = (await response.json()) as {
      allocationCategories?: string[];
      basemapSources?: DeclaredBasemapSource[];
    };
    return {
      allocationCategories: answered.allocationCategories ?? [],
      basemapSources: basemapSourcesFrom(answered.basemapSources ?? []),
    };
  },

  /** Asks the service to send a code to the address, which is the step that establishes somebody reads
   *  what is sent there. Answers nothing: what happens next is the person reading their mail. */
  askForCode: async (emailAddress: string): Promise<void> => {
    const response = await fetch(`${serviceAddress()}/submissions/verification`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ emailAddress }),
    });

    if (!response.ok) throw new Error(await refusalFrom(response));
  },

  /** The code is spent on a ticket and the ticket on the submission, in one act: a ticket lasts minutes
   *  and is no use to the page beyond the post it was got for. */
  submit: async (submission: SubmissionDocument, code: string): Promise<SubmissionAccepted> => {
    const base = serviceAddress();

    const exchanged = await fetch(`${base}/submissions/ticket`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ emailAddress: submission.contact.emailAddress, code }),
    });
    if (!exchanged.ok) throw new Error(await refusalFrom(exchanged));

    const response = await fetch(`${base}/submissions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', [TICKET_HEADER]: (await exchanged.json()).ticket },
      body: JSON.stringify(submission),
    });

    if (!response.ok) throw new Error(await refusalFrom(response));
    return response.json();
  },
};

function serviceAddress(): string {
  const base = intakeUrl().replace(/\/$/, '');
  if (!base) throw new Error('The intake service address is not configured (set VITE_INTAKE_URL).');
  return base;
}

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
