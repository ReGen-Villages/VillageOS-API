import type { SubmissionDocument } from '../intake/submissionDraft';
import type { BasemapSource, DeclaredBasemapSource } from '../types/basemap';
import type { DeclaredTheme } from '@tendril-ui/tendril';
import { basemapSourcesFrom } from '../utils/basemapSources';
import type { TermWording } from '@tendril-ui/tendril';
import type { AreaMethod } from '../utils/borderArea';
import type { BoundaryPoint } from '../utils/parcelGeometry';
import i18n from '../i18n';
import { refusalFrom } from './refusals';

/** Only a reference to quote, because the route is anonymous and a stranger is not told the shape of the
 *  model they submitted into. */
export interface SubmissionAccepted {
  reference: string;
}

/**
 * The intake service is not reached through the broker's endpoint forward, because that forward reads its
 * target from the model, so anything the model named would be in reach of any caller. Intake holds its own
 * credential, which lets it take a submission from someone who holds none. Its address is configuration
 * because one deployment runs it beside the broker and another elsewhere.
 */
const intakeUrl = () => (import.meta.env.VITE_INTAKE_URL as string | undefined) || '';

/** The ticket is signed against the address it was issued for, so a submission naming another address
 *  carries no ticket of its own. */
const TICKET_HEADER = 'X-Submission-Ticket';

/** A page with no credential cannot read the model, so the service reads it for the form. Which sources
 *  are usable is judged here by the same rule the planner's page uses. */
export interface FormOptions {
  allocationCategories: string[];
  basemapSources: BasemapSource[];
  /** The model's own, so a form cannot offer a term a submission is then refused for. Empty where a
   *  deployment declares no hazards, which draws one step fewer rather than refusing to answer. */
  hazardTypes: string[];
  hazardLevels: string[];
  /** Offered before anybody states a programme, as each category Thing states it. Empty where the model
   *  states no defaults, which starts a page with nothing chosen. */
  defaultProgramme: { category: string; sharePercent: number }[];
  /** So a page draws no search box for a model with no gazetteer, and asks no register that is absent. */
  parcelLookup: boolean;
  placeSearch: boolean;
  /** The themes a report section may name, which give it a tile. Empty where the model declares none, which
   *  draws every section as a list. */
  themes: DeclaredTheme[];
  /** The kinds a planner may give each named shape of a layer file. Empty where the model declares no site layer. */
  layerKinds: { name: string; colour: string | null; themes: string[] }[];
  /** Keyed by the names the lists above offer. A term the model words in no language is absent and shown by its name. */
  wording: TermWording;
  /** Null where the service names a method this page does not measure with, so the page shows no area rather than
   *  one the submission would then disagree with. */
  areaMethod: AreaMethod | null;
}

const AREA_METHODS: readonly AreaMethod[] = ['meanSphereAreaMethod', 'wgs84EllipsoidAreaMethod'];

export interface ViewBox {
  southLatitude: number;
  westLongitude: number;
  northLatitude: number;
  eastLongitude: number;
}

export interface RegisteredPlot {
  boundary: BoundaryPoint[];
  identifier: string | null;
  areaHectares: number | null;
}

export interface BorderElevation {
  siteMetres: number;
  lowestMetres: number | null;
  highestMetres: number | null;
  attribution: string | null;
}

export interface LargestArea {
  name: string | null;
  boundary: BoundaryPoint[];
  hectares: number;
}

/** The register's credit is shown wherever the boundary is drawn, as a basemap's credit is. */
interface FetchedParcel {
  boundary: { latitude: number; longitude: number }[];
  attribution: string | null;
}

export interface FoundPlace {
  name: string;
  latitude: number;
  longitude: number;
}

export const intakeApi = {
  /** Decides whether the wizard is offered, because one that collects a submission it cannot post is worse than none. */
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
      areaMethod?: string;
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
      areaMethod: AREA_METHODS.find((method) => method === answered.areaMethod) ?? null,
    };
  },

  /** No plots where the model has no register for the view, which the page shows as a step it cannot take yet. */
  plotsInView: async (
    view: ViewBox,
    zoom: number,
    ticket: string,
  ): Promise<{ plots: RegisteredPlot[] | null; attributions: string[]; ticket: string }> => {
    const response = await intakeRequest('/submissions/plots-in-view', { body: { ...view, zoom }, ticket, passNotFound: true });
    if (response.status === 404) return { plots: null, attributions: [], ticket };
    const answered = (await response.json()) as { plots: RegisteredPlot[]; attributions: string[] };
    return { plots: answered.plots, attributions: answered.attributions, ticket: renewedTicket(response, ticket) };
  },

  borderElevation: async (
    boundary: readonly BoundaryPoint[],
    ticket: string,
  ): Promise<{ elevation: BorderElevation | null; ticket: string }> => {
    const response = await intakeRequest('/submissions/border-elevation', { body: { boundary }, ticket, passNotFound: true });
    if (response.status === 404) return { elevation: null, ticket };
    const answered = (await response.json()) as {
      elevationMetres: number;
      elevationMinimumMetres: number | null;
      elevationMaximumMetres: number | null;
      attribution: string | null;
    };
    return {
      elevation: {
        siteMetres: answered.elevationMetres,
        lowestMetres: answered.elevationMinimumMetres,
        highestMetres: answered.elevationMaximumMetres,
        attribution: answered.attribution,
      },
      ticket: renewedTicket(response, ticket),
    };
  },

  /** The service reads the file and picks its largest area, measured with the model's method, so the page reads no
   *  file format itself. The address is sent because a ticket is renewed against one. */
  readFile: async (
    file: File,
    emailAddress: string,
    ticket: string,
  ): Promise<{ largestArea: LargestArea | null; ticket: string }> => {
    const form = new FormData();
    form.append('file', file);
    form.append('emailAddress', emailAddress);
    const response = await intakeRequest('/submissions/read-file', { body: form, ticket });
    const answered = (await response.json()) as {
      shapes: { name: string | null }[];
      largestArea: { shape: number; boundary: BoundaryPoint[]; hectares: number } | null;
    };
    const largest = answered.largestArea;
    return {
      largestArea:
        largest === null
          ? null
          : { name: answered.shapes[largest.shape].name, boundary: largest.boundary, hectares: largest.hectares },
      ticket: renewedTicket(response, ticket),
    };
  },

  sendDocument: async (submissionId: string, file: File, ticket: string): Promise<string> => {
    const form = new FormData();
    form.append('file', file);
    const response = await intakeRequest(`/submissions/${encodeURIComponent(submissionId)}/documents`, { body: form, ticket });
    return renewedTicket(response, ticket);
  },

  /** Null for no register, a position outside every register, or no parcel there, because in each case the
   *  page does the same thing and lets the person draw. */
  parcelAt: async (latitude: number, longitude: number): Promise<FetchedParcel | null> => {
    const response = await intakeRequest('/submissions/parcel-at-position', { body: { latitude, longitude }, passNotFound: true });
    if (response.status === 404) return null;
    const answered = (await response.json()) as FetchedParcel;
    return { boundary: answered.boundary ?? [], attribution: answered.attribution ?? null };
  },

  /** None where the model registers no gazetteer. */
  searchPlaces: async (query: string): Promise<FoundPlace[]> => {
    const response = await intakeRequest('/submissions/place-search', { body: { query }, passNotFound: true });
    if (response.status === 404) return [];
    const answered = (await response.json()) as { places?: FoundPlace[] };
    return answered.places ?? [];
  },

  /** The code proves somebody reads what is sent to the address. */
  askForCode: async (emailAddress: string): Promise<void> => {
    await intakeRequest('/submissions/verification', { body: { emailAddress } });
  },

  /** In one call, because a ticket lasts minutes and is no use beyond the post it was got for. */
  submit: async (submission: SubmissionDocument, code: string): Promise<SubmissionAccepted> => {
    const ticket = await intakeApi.exchangeTicket(submission.contact.emailAddress, code);
    return (await intakeApi.submitWithTicket(submission, ticket)).accepted;
  },

  /** Apart from submitting, because a page that posts again and again exchanges one code and then uses the
   *  renewed tickets. */
  exchangeTicket: async (emailAddress: string, code: string): Promise<string> => {
    const exchanged = await intakeRequest('/submissions/ticket', { body: { emailAddress, code } });
    return (await exchanged.json()).ticket;
  },

  /** Each accepted post answers with a fresh ticket for the caller's next one, so a page adjusting sliders
   *  keeps going while an abandoned ticket still expires at its own age. */
  submitWithTicket: async (
    submission: SubmissionDocument,
    ticket: string,
  ): Promise<{ accepted: SubmissionAccepted; ticket: string }> => {
    const response = await intakeRequest('/submissions', { body: submission, ticket });
    return { accepted: await response.json(), ticket: renewedTicket(response, ticket) };
  },
};

/** A tile path is the intake service's route to imagery it passes on, so a page holds no provider address;
 *  the public pages are served from another site, so the path is resolved against the service. */
function servedByTheService(source: BasemapSource): BasemapSource {
  return source.kind === 'raster' && source.tileUrl.startsWith('/')
    ? { ...source, tileUrl: `${intakeServiceAddress()}${source.tileUrl}` }
    : source;
}

export async function intakeRequest(
  path: string,
  { body, ticket, passNotFound = false }: { body?: unknown; ticket?: string; passNotFound?: boolean } = {},
): Promise<Response> {
  // A form sets its own content type, with the boundary between its parts.
  const isForm = body instanceof FormData;
  const response = await fetch(`${intakeServiceAddress()}${path}`, {
    method: body === undefined ? 'GET' : 'POST',
    headers: {
      ...(body === undefined || isForm ? {} : { 'Content-Type': 'application/json' }),
      ...(ticket === undefined ? {} : { [TICKET_HEADER]: ticket }),
    },
    ...(body === undefined ? {} : { body: isForm ? body : JSON.stringify(body) }),
  });
  if (passNotFound && response.status === 404) return response;
  if (!response.ok) throw await refusalFrom(response);
  return response;
}

/** The service sends a new ticket with each request it accepts; when it sends none, the held ticket is still good. */
export function renewedTicket(response: Response, held: string): string {
  return response.headers.get(TICKET_HEADER) ?? held;
}

function intakeServiceAddress(): string {
  const base = intakeUrl().replace(/\/$/, '');
  if (!base) throw new Error(i18n.t('intake.serviceNotConfigured', { setting: 'VITE_INTAKE_URL' }));
  return base;
}

