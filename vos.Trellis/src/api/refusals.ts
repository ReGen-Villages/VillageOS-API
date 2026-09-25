import i18n from '../i18n';

/**
 * Every code the intake service names a refusal by. The service lists the same ones in
 * vos.Service.Intake/Models/Refusal.cs, and a test on that side fails when the two lists differ.
 */
export const REFUSAL_CODES = [
  'tooManyRequests', 'serviceUnavailable', 'codeSendingUnavailable', 'submissionsUnavailable', 'filesUnavailable',
  'positionMissing', 'noParcel', 'queryMissing', 'noSearch', 'noBasemap',
  'emailAddressMissing', 'codesExhausted', 'codeNotAccepted', 'ticketMissing', 'ticketExpired', 'addressNotVerified',
  'submissionTooLarge', 'submissionEmpty', 'submissionUnreadable', 'identifierInvalid', 'emailAddressMalformed',
  'fieldMissing', 'fieldTooLong', 'fieldTooMany', 'fieldOutOfRange', 'termUnknown', 'sharesDisagree',
  'hazardsDisagree', 'sourceDescribedTwice', 'boundaryTooFewCorners', 'modelRefused',
  'referenceAndAddressNeeded', 'notYourSubmission', 'reductionMalformed',
  'fileNotAForm', 'noFile', 'fileTooLarge', 'descriptionTooLong', 'filesNotTaken',
] as const;

export type RefusalCode = (typeof REFUSAL_CODES)[number];

/** A refusal from the intake service, worded in the reader's language where the pages know its code. */
class ServiceRefusal extends Error {
  readonly code: RefusalCode | null;

  constructor(message: string, code: RefusalCode | null) {
    super(message);
    this.name = 'ServiceRefusal';
    this.code = code;
  }
}

interface RefusalBody {
  code?: string;
  error?: string;
  values?: Record<string, unknown>;
  detail?: string;
  title?: string;
}

function known(code: string | undefined): RefusalCode | null {
  return (REFUSAL_CODES as readonly string[]).includes(code ?? '') ? (code as RefusalCode) : null;
}

/** What the service refused, in the reader's words where the code is known, else in the service's own,
 *  else by the status it answered with. */
export function refusalIn(body: RefusalBody | null, status: number): ServiceRefusal {
  const code = known(body?.code);
  const message = code
    ? i18n.t(`refusal.${code}`, body?.values ?? {})
    : body?.error ?? body?.detail ?? body?.title ?? i18n.t('intake.refusedWithStatus', { status });
  return new ServiceRefusal(message, code);
}

export async function refusalFrom(response: Response): Promise<ServiceRefusal> {
  try {
    return refusalIn(await response.json(), response.status);
  } catch {
    return refusalIn(null, response.status);
  }
}

/** Whether the refusal is the ticket's rather than the submission's: the one a person mends by
 *  confirming their address again. */
export function isTicketRefusal(error: unknown): boolean {
  return error instanceof ServiceRefusal && (error.code === 'ticketMissing' || error.code === 'ticketExpired');
}
