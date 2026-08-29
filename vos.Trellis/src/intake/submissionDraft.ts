/**
 * What the land-intake wizard is holding, and everything that can be worked out from it without a
 * screen: the steps, the area unit, the programme split, and the document the intake service is posted.
 *
 * The draft keeps what the planner typed rather than what it parses to. A half-typed figure is a normal
 * state of a form — a lone minus sign, a trailing decimal point — and a draft holding numbers would have
 * to decide what those mean on every keystroke. Parsing happens once, on the way out, where a figure
 * that is not a figure is absent rather than zero.
 */

import { onEarth } from '../utils/mapLink';
import { draftSquareAround, type BoundaryPoint } from '../utils/parcelGeometry';

/** How the land area is being typed. What is stored and submitted is always hectares: a submission read
 *  in acres and recorded as hectares is a site two and a half times too small, and nothing downstream
 *  could tell. */
export type AreaUnit = 'hectares' | 'acres';

export const HECTARES_PER_ACRE = 0.40468564224;

export const STEPS = ['project', 'contact', 'location', 'programme', 'parcel', 'hazards'] as const;

export type StepId = (typeof STEPS)[number];

/**
 * How a boundary came to be, in the words the land-intake template declares and the intake service
 * resolves against the model. The wizard states this itself rather than asking — it knows whether it
 * generated the square — so these are the two declared terms it can truthfully submit.
 */
export const BOUNDARY_DRAWN_BY_HAND = 'drawn-by-hand';
export const BOUNDARY_GENERATED_FROM_STATED_AREA = 'generated-from-stated-area';

export type BoundaryObtainedBy =
  | typeof BOUNDARY_DRAWN_BY_HAND
  | typeof BOUNDARY_GENERATED_FROM_STATED_AREA;

/** The share of the parcel each chosen category takes, by the category's own name in the model. */
export type ProgrammeShares = Readonly<Record<string, number>>;

/** A level the submitter reports, by the hazard the model names. Only what they marked is here.
 *  Both words are the model's, so a submission carries no term the model would refuse. */
export type ReportedHazards = Readonly<Record<string, string>>;

export interface SubmissionDraft {
  /** Minted once, when the draft starts. Every Thing a submission creates derives its identity from
   *  this, so a planner who submits twice corrects one site rather than proposing a second. */
  readonly submissionId: string;
  readonly projectName: string;
  readonly country: string;
  readonly nearestCity: string;
  readonly existingDataNotes: string;
  readonly contactName: string;
  readonly relationshipToProject: string;
  readonly emailAddress: string;
  readonly phoneNumber: string;
  readonly siteName: string;
  readonly latitude: string;
  readonly longitude: string;
  readonly statedArea: string;
  readonly areaUnit: AreaUnit;
  readonly population: string;
  readonly householdSize: string;
  readonly shares: ProgrammeShares;
  readonly boundary: readonly BoundaryPoint[];
  readonly boundarySource: BoundaryObtainedBy | null;
  /** What the submitter says they have seen, keyed by the hazard the model names. A hazard they say
   *  nothing about is absent rather than held at a level nobody reported. */
  readonly reportedHazards: ReportedHazards;
  readonly visited: readonly StepId[];
}

/** The document the intake service takes. It requires the identifier, the site's name, the project, and a
 *  contact carrying a name and an email address; every other field is optional. A field it does not
 *  declare is refused outright rather than dropped, so nothing is sent here that the service has no home
 *  for. */
export interface SubmissionDocument {
  submissionId: string;
  project: { name: string; country?: string; nearestCity?: string; existingDataNotes?: string };
  contact: { name: string; relationshipToProject?: string; emailAddress: string; phoneNumber?: string };
  site: {
    name: string;
    latitude?: number;
    longitude?: number;
    statedAreaHectares?: number;
    population?: number;
    householdSize?: number;
  };
  parcel?: { boundarySource: string; boundary: { latitude: number; longitude: number }[] };
  allocations?: { category: string; sharePct: number }[];
  hazards?: { hazardType: string; reportedLevel: string }[];
}

export function emptyDraft(submissionId: string): SubmissionDraft {
  return {
    submissionId,
    projectName: '',
    country: '',
    nearestCity: '',
    existingDataNotes: '',
    contactName: '',
    relationshipToProject: '',
    emailAddress: '',
    phoneNumber: '',
    siteName: '',
    latitude: '',
    longitude: '',
    statedArea: '',
    areaUnit: 'hectares',
    population: '',
    householdSize: '',
    shares: {},
    boundary: [],
    boundarySource: null,
    reportedHazards: {},
    visited: ['project'],
  };
}

// ── The area, and the unit it is being typed in ──────────────────────────────

function toHectares(value: number, unit: AreaUnit): number {
  return unit === 'acres' ? value * HECTARES_PER_ACRE : value;
}

export function fromHectares(hectares: number, unit: AreaUnit): number {
  return unit === 'acres' ? hectares / HECTARES_PER_ACRE : hectares;
}

/** The stated area in hectares, whatever unit it was typed in, or nothing where no usable figure was
 *  typed. */
export function statedAreaHectares(draft: SubmissionDraft): number | null {
  const typed = positiveNumberFrom(draft.statedArea);
  return typed === null ? null : toHectares(typed, draft.areaUnit);
}

/** A figure a planner typed, where a negative one would be a mistake rather than a value: an area, a
 *  population, a household size. Zero is allowed through — somebody stated it — but a word, an empty
 *  box or a minus sign is absent. */
function positiveNumberFrom(typed: string): number | null {
  const value = numberFrom(typed);
  return value === null || value < 0 ? null : value;
}

/** A figure that may legitimately be negative, which is every coordinate west of Greenwich or south of
 *  the equator. */
function numberFrom(typed: string): number | null {
  const text = typed.trim();
  if (text.length === 0) return null;
  const value = Number(text);
  return Number.isFinite(value) ? value : null;
}

/** Where the site sits, which is what the map is centred on — or nowhere, while either half of the
 *  position is missing, unreadable, or off the Earth. */
export function coordinatesFrom(draft: SubmissionDraft): { latitude: number; longitude: number } | null {
  const latitude = numberFrom(draft.latitude);
  const longitude = numberFrom(draft.longitude);
  if (latitude === null || longitude === null || !onEarth(latitude, longitude)) return null;
  return { latitude, longitude };
}

// ── The programme split ──────────────────────────────────────────────────────

const WHOLE_PARCEL = 100;

/** Choosing a category takes an equal share and leaves the rest holding the same proportions to each
 *  other as before. */
export function withCategoryChosen(shares: ProgrammeShares, category: string): ProgrammeShares {
  if (category in shares) return shares;
  const chosenAlready = Object.keys(shares).length;
  if (chosenAlready === 0) return { [category]: WHOLE_PARCEL };

  const share = WHOLE_PARCEL / (chosenAlready + 1);
  return { ...scaledTo(shares, WHOLE_PARCEL - share), [category]: share };
}

/** Dropping a category gives its share back to the rest in proportion, so the split still describes the
 *  whole parcel. Dropping the last one leaves nothing chosen, which is a split of nothing rather than a
 *  split by zero. */
export function withCategoryDropped(shares: ProgrammeShares, category: string): ProgrammeShares {
  if (!(category in shares)) return shares;
  return scaledTo(without(shares, category), WHOLE_PARCEL);
}

/** Setting one category's share moves the difference across the others in proportion. A share outside
 *  the parcel is not a share, so it is held at the nearest end rather than refused mid-typing. */
export function withShareSet(shares: ProgrammeShares, category: string, share: number): ProgrammeShares {
  if (!(category in shares)) return shares;
  const held = Math.min(Math.max(share, 0), WHOLE_PARCEL);
  return { ...scaledTo(without(shares, category), WHOLE_PARCEL - held), [category]: held };
}

/**
 * The shares as whole percentages that still add to a hundred.
 *
 * Rounding each share on its own does not: three categories of a third each show as 33, and a planner
 * reading 99 concludes the split is short of the parcel it is meant to describe. The whole numbers are
 * handed out by largest remainder instead — everyone gets their floor, and the percentages left over go
 * to whoever was rounded down hardest.
 */
export function wholePercentages(shares: ProgrammeShares): Readonly<Record<string, number>> {
  const names = Object.keys(shares);
  if (names.length === 0) return {};

  const floors = Object.fromEntries(names.map((name) => [name, Math.floor(shares[name])]));
  const givenOut = names.reduce((sum, name) => sum + floors[name], 0);
  const byRemainder = [...names].sort((left, right) => (shares[right] % 1) - (shares[left] % 1));

  // Each share loses less than a whole percentage to its floor, so there are always fewer left over
  // than there are categories and nobody is handed two.
  for (const name of byRemainder.slice(0, WHOLE_PARCEL - givenOut)) floors[name] += 1;
  return floors;
}

function without(shares: ProgrammeShares, category: string): ProgrammeShares {
  return Object.fromEntries(Object.entries(shares).filter(([name]) => name !== category));
}

/** Scaled to a total, keeping the proportions between them. A set that adds to nothing has no
 *  proportions to keep, so it is split equally — which is also what keeps a total of zero from dividing
 *  into a not-a-number and putting one on screen. */
function scaledTo(shares: ProgrammeShares, total: number): ProgrammeShares {
  const names = Object.keys(shares);
  if (names.length === 0) return {};

  const held = names.reduce((sum, name) => sum + shares[name], 0);
  const equalShare = total / names.length;
  return Object.fromEntries(
    names.map((name) => [name, held > 0 ? (shares[name] / held) * total : equalShare]),
  );
}

// ── The parcel boundary ──────────────────────────────────────────────────────

/** A square of the stated area centred on the site, for the planner to drag onto the real boundary —
 *  or nothing, while the position or a positive stated area is still missing. */
export function boundaryDrafted(draft: SubmissionDraft): Partial<SubmissionDraft> | null {
  const position = coordinatesFrom(draft);
  const hectares = statedAreaHectares(draft);
  if (position === null || hectares === null) return null;
  const boundary = draftSquareAround(position, hectares);
  return boundary.length === 0
    ? null
    : { boundary, boundarySource: BOUNDARY_GENERATED_FROM_STATED_AREA };
}

/** Whatever the boundary was before, a corner the planner placed or moved makes the whole of it the
 *  planner's own assertion — a dragged draft square must not go on reading as generated. */
export function boundaryDrawn(boundary: readonly BoundaryPoint[]): Partial<SubmissionDraft> {
  return { boundary, boundarySource: BOUNDARY_DRAWN_BY_HAND };
}

export function boundaryCleared(): Partial<SubmissionDraft> {
  return { boundary: [], boundarySource: null };
}

// ── Moving between steps ─────────────────────────────────────────────────────

export function withStepVisited(draft: SubmissionDraft, step: StepId): SubmissionDraft {
  return draft.visited.includes(step) ? draft : { ...draft, visited: [...draft.visited, step] };
}

// ── What is posted ───────────────────────────────────────────────────────────

/**
 * The site's name and the project's are what Things are created under, and a submission is reviewed by
 * somebody who has to be able to say what was decided — so a draft is held back until it names who to
 * tell and where.
 *
 * Whether the address is one is left to the service. A second rule here could only disagree with it, and
 * a refusal names the field to correct.
 */
export function readyToSubmit(draft: SubmissionDraft): boolean {
  return [draft.siteName, draft.projectName, draft.contactName, draft.emailAddress].every(
    (given) => given.trim().length > 0,
  );
}

/** What a person marks against one hazard. An empty word clears it: saying nothing about a hazard has
 *  to stay possible after saying something, or a mis-click is a report they cannot take back. */
export function withHazardReported(
  draft: SubmissionDraft,
  hazardType: string,
  reportedLevel: string,
): SubmissionDraft {
  const reported = { ...draft.reportedHazards };
  if (reportedLevel.length === 0) delete reported[hazardType];
  else reported[hazardType] = reportedLevel;
  return { ...draft, reportedHazards: reported };
}

export function documentFrom(draft: SubmissionDraft): SubmissionDocument {
  const allocations = Object.entries(draft.shares).map(([category, sharePct]) => ({ category, sharePct }));
  const hazards = Object.entries(draft.reportedHazards)
    .filter(([, reportedLevel]) => reportedLevel.length > 0)
    .map(([hazardType, reportedLevel]) => ({ hazardType, reportedLevel }));

  return {
    submissionId: draft.submissionId,
    project: {
      name: draft.projectName.trim(),
      ...given({
        country: draft.country,
        nearestCity: draft.nearestCity,
        existingDataNotes: draft.existingDataNotes,
      }),
    },
    contact: {
      name: draft.contactName.trim(),
      emailAddress: draft.emailAddress.trim(),
      ...given({
        relationshipToProject: draft.relationshipToProject,
        phoneNumber: draft.phoneNumber,
      }),
    },
    site: {
      name: draft.siteName.trim(),
      ...defined('latitude', numberFrom(draft.latitude)),
      ...defined('longitude', numberFrom(draft.longitude)),
      ...defined('statedAreaHectares', statedAreaHectares(draft)),
      ...defined('population', wholeNumberFrom(draft.population)),
      ...defined('householdSize', positiveNumberFrom(draft.householdSize)),
    },
    // Rebuilt corner by corner so a stored corner carrying anything beyond its coordinates does not
    // post it. Below three corners a boundary encloses nothing and is not a parcel.
    ...(draft.boundary.length >= 3 &&
      draft.boundarySource !== null && {
        parcel: {
          boundarySource: draft.boundarySource,
          boundary: draft.boundary.map((corner) => ({
            latitude: corner.latitude,
            longitude: corner.longitude,
          })),
        },
      }),
    ...(allocations.length > 0 && { allocations }),
    ...(hazards.length > 0 && { hazards }),
  };
}

/** A count of people is a whole number: a population typed with a decimal point is a slip, and rounding
 *  it silently would record a figure nobody stated. */
function wholeNumberFrom(typed: string): number | null {
  const value = positiveNumberFrom(typed);
  return value !== null && Number.isInteger(value) ? value : null;
}

function defined(name: string, value: number | null): Record<string, number> {
  return value === null ? {} : { [name]: value };
}

/** The optional text fields a planner filled in. A blank one is left out rather than sent as an empty
 *  string, so nothing writes a value nobody typed. */
function given<T extends Record<string, string>>(fields: T): Partial<T> {
  return Object.fromEntries(
    Object.entries(fields)
      .filter(([, value]) => value.trim().length > 0)
      .map(([name, value]) => [name, value.trim()]),
  ) as Partial<T>;
}

// ── Keeping the draft across a closed tab ────────────────────────────────────

const DRAFT_KEY_PREFIX = 'vos-intake-draft:';

function draftKey(draftOwner: string): string {
  return DRAFT_KEY_PREFIX + draftOwner;
}

/**
 * Per owner: a planner's draft describes land being proposed into one model and they may hold a half-
 * filled submission in each, so the model owns it; a public form has one submitter in one browser, so
 * the form does.
 *
 * The two fields that are not plain text are checked rather than trusted. Browser storage is edited by
 * hand and survives a deployment, and a draft whose split is a word instead of a set of shares takes the
 * page down every time it is opened — with no way left to reach the button that would clear it.
 */
export function loadDraft(draftOwner: string): SubmissionDraft | null {
  const stored = localStorage.getItem(draftKey(draftOwner));
  if (!stored) return null;
  try {
    const parsed = JSON.parse(stored) as Partial<SubmissionDraft>;
    if (typeof parsed?.submissionId !== 'string') return null;
    const empty = emptyDraft(parsed.submissionId);
    return {
      ...empty,
      ...parsed,
      shares: sharesIn(parsed.shares) ?? empty.shares,
      ...(parcelIn(parsed.boundary, parsed.boundarySource) ?? {
        boundary: empty.boundary,
        boundarySource: empty.boundarySource,
      }),
      visited: stepsIn(parsed.visited) ?? empty.visited,
    };
  } catch {
    return null;
  }
}

/** The stored boundary and its origin stand or fall together: corners whose origin is missing, or an
 *  origin the wizard could never have written, would post a claim nobody made. */
function parcelIn(
  storedBoundary: unknown,
  storedSource: unknown,
): Pick<SubmissionDraft, 'boundary' | 'boundarySource'> | null {
  if (!Array.isArray(storedBoundary)) return null;
  const corners = storedBoundary.every(
    (corner: Partial<BoundaryPoint> | null) =>
      typeof corner?.latitude === 'number' &&
      Number.isFinite(corner.latitude) &&
      typeof corner.longitude === 'number' &&
      Number.isFinite(corner.longitude),
  );
  if (!corners) return null;
  if (storedBoundary.length === 0) return { boundary: [], boundarySource: null };
  return storedSource === BOUNDARY_DRAWN_BY_HAND || storedSource === BOUNDARY_GENERATED_FROM_STATED_AREA
    ? { boundary: storedBoundary as BoundaryPoint[], boundarySource: storedSource }
    : null;
}

function sharesIn(stored: unknown): ProgrammeShares | null {
  if (stored === null || typeof stored !== 'object') return null;
  const entries = Object.entries(stored);
  return entries.every(([, share]) => typeof share === 'number' && Number.isFinite(share))
    ? (stored as ProgrammeShares)
    : null;
}

function stepsIn(stored: unknown): StepId[] | null {
  if (!Array.isArray(stored)) return null;
  const steps = stored.filter((step): step is StepId => (STEPS as readonly string[]).includes(step));
  return steps.length === 0 ? null : steps;
}

export function saveDraft(draftOwner: string, draft: SubmissionDraft): void {
  localStorage.setItem(draftKey(draftOwner), JSON.stringify(draft));
}

export function clearDraft(draftOwner: string): void {
  localStorage.removeItem(draftKey(draftOwner));
}
