import { apiClient } from './client';
import type { SubmissionDocument } from '../pages/intakeWizard';

/** What the intake service made of a submission: the Things it composed it into. */
export interface SubmissionAccepted {
  siteId: string;
  studyId: string;
  parcelId?: string | null;
}

/**
 * The intake service is a program of its own rather than a service reached through the broker's endpoint
 * forward, and deliberately so: that route resolves where to forward from data in the model, so anything
 * the model named would be within reach of whoever could call it. Intake holds its own credential and its
 * own address, which is what lets the anonymous route be widened later without widening every endpoint.
 *
 * Its address is configuration for the same reason the ingestion service's is — one deployment may run
 * it beside the broker and another somewhere else entirely.
 */
const intakeUrl = () => (import.meta.env.VITE_INTAKE_URL as string | undefined) || '';

export const intakeApi = {
  /** Whether an intake address is configured, which is what decides whether the wizard is offered at
   *  all: a wizard that collects a submission it cannot post is worse than no wizard. */
  configured: () => intakeUrl().length > 0,

  submit: async (submission: SubmissionDocument): Promise<SubmissionAccepted> => {
    const base = intakeUrl();
    if (!base) throw new Error('The intake service address is not configured (set VITE_INTAKE_URL).');

    const token = await apiClient.ensureToken();
    const response = await fetch(`${base.replace(/\/$/, '')}/submissions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
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
