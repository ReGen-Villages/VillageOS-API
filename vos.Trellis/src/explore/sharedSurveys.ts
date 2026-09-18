/**
 * What the surveys step holds between choosing files and seeing them listed: each chosen file with the
 * description a person gives it and how far its sending has got. The bytes go to the intake service,
 * which keeps them beside itself and gives the model a Thing for each; what the list shows afterwards
 * is what the model holds, read back through the same service.
 */

/** The most a file may be, as the intake service holds it. A file over this is refused here before a
 *  byte is sent, with the same reason the service would give. */
export const MAXIMUM_SURVEY_BYTES = 25 * 1024 * 1024;

export const MAXIMUM_SURVEY_MEGABYTES = MAXIMUM_SURVEY_BYTES / (1024 * 1024);

/** One file the model holds for the submission, as the service lists it. */
export interface SharedSurvey {
  id: string;
  fileName: string;
  description: string | null;
  contentType: string | null;
  sizeBytes: number | null;
  sharedAt: string | null;
}

export type QueuedSurveyState = 'waiting' | 'sending' | 'shared' | 'refused';

export interface QueuedSurvey {
  /** Of the page's own: two chosen files may share a name. */
  readonly key: string;
  readonly file: File;
  readonly description: string;
  readonly state: QueuedSurveyState;
  /** The share of the bytes the browser has sent, 0 to 1. */
  readonly progress: number;
  readonly reason: string | null;
}

export function tooLarge(file: File): boolean {
  return file.size > MAXIMUM_SURVEY_BYTES;
}

/** Chosen files joined to the queue. One over the limit is queued already refused, so the person sees
 *  why beside the others rather than the whole choice being turned away. */
export function withChosen(
  queue: readonly QueuedSurvey[], chosen: readonly File[], mintKey: () => string, tooLargeReason: string,
): QueuedSurvey[] {
  return [
    ...queue,
    ...chosen.map((file): QueuedSurvey => ({
      key: mintKey(),
      file,
      description: '',
      progress: 0,
      ...(tooLarge(file)
        ? { state: 'refused', reason: tooLargeReason }
        : { state: 'waiting', reason: null }),
    })),
  ];
}

export function withDescription(queue: readonly QueuedSurvey[], key: string, description: string): QueuedSurvey[] {
  return queue.map((queued) => (queued.key === key ? { ...queued, description } : queued));
}

export function asSending(queue: readonly QueuedSurvey[], key: string, progress: number): QueuedSurvey[] {
  return queue.map((queued) =>
    queued.key === key ? { ...queued, state: 'sending', progress: Math.min(1, Math.max(0, progress)), reason: null } : queued);
}

export function asShared(queue: readonly QueuedSurvey[], key: string): QueuedSurvey[] {
  return queue.map((queued) => (queued.key === key ? { ...queued, state: 'shared', progress: 1, reason: null } : queued));
}

export function asRefused(queue: readonly QueuedSurvey[], key: string, reason: string): QueuedSurvey[] {
  return queue.map((queued) => (queued.key === key ? { ...queued, state: 'refused', progress: 0, reason } : queued));
}

export function withoutQueued(queue: readonly QueuedSurvey[], key: string): QueuedSurvey[] {
  return queue.filter((queued) => queued.key !== key);
}

/** The files the person may send now: chosen, not yet sent, and not refused before sending. */
export function readyToSend(queue: readonly QueuedSurvey[]): QueuedSurvey[] {
  return queue.filter((queued) => queued.state === 'waiting');
}

/** A file the model now holds joins the list once; the service answers the same document for a share
 *  and for a listing, and a listing read after a share would otherwise show it twice. */
export function withListed(shared: readonly SharedSurvey[], listed: readonly SharedSurvey[]): SharedSurvey[] {
  const known = new Set(shared.map((survey) => survey.id));
  return [...shared, ...listed.filter((survey) => !known.has(survey.id))];
}
