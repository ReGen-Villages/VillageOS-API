import type { SubmissionDocument } from '../intake/submissionDraft';
import type { BasemapSource, DeclaredBasemapSource } from '../types/basemap';
import type { DeclaredTheme } from '@tendril-ui/tendril';
import { basemapSourcesFrom } from '../utils/basemapSources';
import type { TermWording } from '@tendril-ui/tendril';
import i18n from '../i18n';
import { refusalFrom } from './refusals';

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
export const TICKET_HEADER = 'X-Submission-Ticket';

/** What the form draws itself with. A page holding no credential cannot read the model for itself, so
 *  the service reads it and answers with the categories a submission may name and the imagery a map may
 *  draw on. What makes a source usable is judged here, by the same rule the planner's page judges it by. */
export interface FormOptions {
  allocationCategories: string[];
  basemapSources: BasemapSource[];
  /** The hazards a submitter may report on, and the words they may report. Both the model's, so a form
   *  cannot offer a term the submission would then be refused for naming. Empty where a deployment
   *  declares no hazards, which draws one step fewer rather than refusing to answer. */
  hazardTypes: string[];
  hazardLevels: string[];
  /** The split a page offers before anybody has stated a programme, as each category Thing states it.
   *  Empty where the model states no defaults, which starts a page with nothing chosen. */
  defaultProgramme: { category: string; sharePercent: number }[];
  /** Whether the model registers each position lookup, so a page draws only what the service can
   *  honour: no search box over a model with no gazetteer, no fetch attempt against no register. */
  parcelLookup: boolean;
  placeSearch: boolean;
  /** The themes a dashboard section may name, as the model declares them — what faces a tile on the
   *  report. Empty where the model declares none, which draws every section as a list. */
  themes: DeclaredTheme[];
  /** The kinds a planner may give each named shape of a layer file, as the model declares them, with
   *  the colour a map draws that kind in and the names of the themes it is shown under. Empty where the
   *  model declares no site layer. */
  layerKinds: { name: string; colour: string | null; themes: string[] }[];
  /** Each offered term's words by language, under the name the lists above offer. A term the model
   *  words in no language is absent, and is shown by its name. */
  wording: TermWording;
}

/** The legal parcel a register holds at a position, as named pairs, with the register's own credit
 *  line — displayed wherever the boundary is drawn, as a basemap's credit is. */
export interface FetchedParcel {
  boundary: { latitude: number; longitude: number }[];
  attribution: string | null;
}

export interface FoundPlace {
  name: string;
  latitude: number;
  longitude: number;
}

export const intakeApi = {
  /** Whether an intake address is configured, which is what decides whether the wizard is offered at
   *  all: a wizard that collects a submission it cannot post is worse than no wizard. */
  configured: () => intakeUrl().length > 0,

  formOptions: async (): Promise<FormOptions> => {
    const response = await intakeRequest('/submissions/form');
    const answered = (await response.json()) as {
      allocationCategories?: string[];
      basemapSources?: DeclaredBasemapSource[];
      hazardTypes?: string[];
      hazardLevels?: string[];
      defaultProgramme?: { category: string; sharePercent: number }[];
      parcelLookup?: boolean;
      placeSearch?: boolean;
      themes?: Partial<DeclaredTheme>[];
      layerKinds?: { name?: string; colour?: string | null; themes?: string[] }[];
      wording?: TermWording;
    };
    return {
      allocationCategories: answered.allocationCategories ?? [],
      basemapSources: basemapSourcesFrom(answered.basemapSources ?? []).map(servedByTheService),
      hazardTypes: answered.hazardTypes ?? [],
      hazardLevels: answered.hazardLevels ?? [],
      defaultProgramme: answered.defaultProgramme ?? [],
      parcelLookup: answered.parcelLookup ?? false,
      placeSearch: answered.placeSearch ?? false,
      wording: answered.wording ?? {},
      themes: (answered.themes ?? []).map((theme) => ({
        name: theme.name ?? '',
        colour: theme.colour ?? null,
        icon: theme.icon ?? null,
        order: theme.order ?? null,
      })),
      layerKinds: (answered.layerKinds ?? []).map((kind) => ({
        name: kind.name ?? '',
        colour: kind.colour ?? null,
        themes: kind.themes ?? [],
      })),
    };
  },

  /** The legal parcel at a position, or null where none is available — a deployment registering no
   *  land register, a position outside every register's bounds, and a register holding no parcel there
   *  are all answered the same way, and the page does the same thing in all three: it lets the person
   *  draw. */
  parcelAt: async (latitude: number, longitude: number): Promise<FetchedParcel | null> => {
    const response = await intakeRequest('/submissions/parcel-at-position', { body: { latitude, longitude }, passNotFound: true });
    if (response.status === 404) return null;
    const answered = (await response.json()) as FetchedParcel;
    return { boundary: answered.boundary ?? [], attribution: answered.attribution ?? null };
  },

  /** Place names for what somebody typed, or none where the model registers no gazetteer. */
  searchPlaces: async (query: string): Promise<FoundPlace[]> => {
    const response = await intakeRequest('/submissions/place-search', { body: { query }, passNotFound: true });
    if (response.status === 404) return [];
    const answered = (await response.json()) as { places?: FoundPlace[] };
    return answered.places ?? [];
  },

  /** Asks the service to send a code to the address, which is the step that establishes somebody reads
   *  what is sent there. Answers nothing: what happens next is the person reading their mail. */
  askForCode: async (emailAddress: string): Promise<void> => {
    await intakeRequest('/submissions/verification', { body: { emailAddress } });
  },

  /** The code is spent on a ticket and the ticket on the submission, in one act: a ticket lasts minutes
   *  and is no use to the page beyond the post it was got for. */
  submit: async (submission: SubmissionDocument, code: string): Promise<SubmissionAccepted> => {
    const ticket = await intakeApi.exchangeTicket(submission.contact.emailAddress, code);
    return (await intakeApi.submitWithTicket(submission, ticket)).accepted;
  },

  /** A code spent on the short-lived ticket the service signs against the address. Held apart from
   *  submitting because a page that keeps re-posting spends one exchange and then rides the renewals. */
  exchangeTicket: async (emailAddress: string, code: string): Promise<string> => {
    const exchanged = await intakeRequest('/submissions/ticket', { body: { emailAddress, code } });
    return (await exchanged.json()).ticket;
  },

  /** Posts under a ticket already held. The service hands a fresh ticket back on every accepted act, so
   *  the caller carries on with whichever came back — a page adjusting sliders stays in the exchange it
   *  completed while an abandoned ticket still dies at its own age. */
  submitWithTicket: async (
    submission: SubmissionDocument,
    ticket: string,
  ): Promise<{ accepted: SubmissionAccepted; ticket: string }> => {
    const response = await intakeRequest('/submissions', { body: submission, ticket });
    return { accepted: await response.json(), ticket: renewedTicket(response, ticket) };
  },
};

/** A tile address that is a path is a route of the intake service — imagery it proxies so a page holds
 *  no provider address — and the public pages are served from a site of their own, so the path is
 *  resolved against the service rather than the page. */
function servedByTheService(source: BasemapSource): BasemapSource {
  return source.kind === 'raster' && source.tileUrl.startsWith('/')
    ? { ...source, tileUrl: `${intakeServiceAddress()}${source.tileUrl}` }
    : source;
}

export async function intakeRequest(
  path: string,
  { body, ticket, passNotFound = false }: { body?: unknown; ticket?: string; passNotFound?: boolean } = {},
): Promise<Response> {
  const response = await fetch(`${intakeServiceAddress()}${path}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: {
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...(ticket === undefined ? {} : { [TICKET_HEADER]: ticket }),
    },
    ...(body === undefined ? {} : { body: JSON.stringify(body) }),
  });
  if (passNotFound && response.status === 404) return response;
  if (!response.ok) throw await refusalFrom(response);
  return response;
}

/** The service sends a new ticket with each request it accepts; when it sends none, the held ticket is still good. */
export function renewedTicket(response: Response, held: string): string {
  return response.headers.get(TICKET_HEADER) ?? held;
}

export function intakeServiceAddress(): string {
  const base = intakeUrl().replace(/\/$/, '');
  if (!base) throw new Error(i18n.t('intake.serviceNotConfigured', { setting: 'VITE_INTAKE_URL' }));
  return base;
}

