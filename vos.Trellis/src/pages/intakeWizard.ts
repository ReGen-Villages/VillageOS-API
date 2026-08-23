/**
 * What the land-intake wizard is holding, and everything that can be worked out from it without a
 * screen: the steps, the area unit, the programme split, and the document the intake service is posted.
 *
 * The draft keeps what the planner typed rather than what it parses to. A half-typed figure is a normal
 * state of a form — a lone minus sign, a trailing decimal point — and a draft holding numbers would have
 * to decide what those mean on every keystroke. Parsing happens once, on the way out, where a figure
 * that is not a figure is absent rather than zero.
 */

/** How the land area is being typed. What is stored and submitted is always hectares: a submission read
 *  in acres and recorded as hectares is a site two and a half times too small, and nothing downstream
 *  could tell. */
export type AreaUnit = 'hectares' | 'acres';

export const HECTARES_PER_ACRE = 0.40468564224;

export const STEPS = ['project', 'contact', 'location', 'programme'] as const;

export type StepId = (typeof STEPS)[number];

/** The share of the parcel each chosen category takes, by the category's own name in the model. */
export type ProgrammeShares = Readonly<Record<string, number>>;

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
  readonly visited: readonly StepId[];
}

/** The document the intake service takes. Every field is optional to it except the identifier and the
 *  site's name, and a field it does not declare is refused outright rather than dropped, so nothing is
 *  sent here that the service has no home for. */
export interface SubmissionDocument {
  submissionId: string;
  project?: { name?: string; country?: string; nearestCity?: string; existingDataNotes?: string };
  contact?: { name?: string; relationshipToProject?: string; emailAddress?: string; phoneNumber?: string };
  site: {
    name: string;
    latitude?: number;
    longitude?: number;
    statedAreaHectares?: number;
    population?: number;
    householdSize?: number;
  };
  allocations?: { category: string; sharePct: number }[];
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

// ── Moving between steps ─────────────────────────────────────────────────────

export function withStepVisited(draft: SubmissionDraft, step: StepId): SubmissionDraft {
  return draft.visited.includes(step) ? draft : { ...draft, visited: [...draft.visited, step] };
}

// ── What is posted ───────────────────────────────────────────────────────────

/** The site's name is what a Thing is created under, so a draft without one cannot be submitted. */
export function readyToSubmit(draft: SubmissionDraft): boolean {
  return draft.siteName.trim().length > 0;
}

export function documentFrom(draft: SubmissionDraft): SubmissionDocument {
  const project = given({
    name: draft.projectName,
    country: draft.country,
    nearestCity: draft.nearestCity,
    existingDataNotes: draft.existingDataNotes,
  });
  const contact = given({
    name: draft.contactName,
    relationshipToProject: draft.relationshipToProject,
    emailAddress: draft.emailAddress,
    phoneNumber: draft.phoneNumber,
  });
  const allocations = Object.entries(draft.shares).map(([category, sharePct]) => ({ category, sharePct }));

  return {
    submissionId: draft.submissionId,
    ...(project && { project }),
    ...(contact && { contact }),
    site: {
      name: draft.siteName.trim(),
      ...defined('latitude', numberFrom(draft.latitude)),
      ...defined('longitude', numberFrom(draft.longitude)),
      ...defined('statedAreaHectares', statedAreaHectares(draft)),
      ...defined('population', wholeNumberFrom(draft.population)),
      ...defined('householdSize', positiveNumberFrom(draft.householdSize)),
    },
    ...(allocations.length > 0 && { allocations }),
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

/** A group of text fields, or nothing where the planner filled none of them in. An empty group would
 *  create a Thing carrying no values, which reads as a contact nobody can be reached at. */
function given<T extends Record<string, string>>(fields: T): Partial<T> | null {
  const filled = Object.entries(fields).filter(([, value]) => value.trim().length > 0);
  return filled.length === 0 ? null : (Object.fromEntries(filled.map(([n, v]) => [n, v.trim()])) as Partial<T>);
}

// ── Keeping the draft across a closed tab ────────────────────────────────────

const DRAFT_KEY_PREFIX = 'vos-intake-draft:';

function draftKey(modelId: string): string {
  return DRAFT_KEY_PREFIX + modelId;
}

/** Per model, because a draft describes land being proposed into one model and a planner may hold a
 *  half-filled submission in each. */
export function loadDraft(modelId: string): SubmissionDraft | null {
  const stored = localStorage.getItem(draftKey(modelId));
  if (!stored) return null;
  try {
    const parsed = JSON.parse(stored) as Partial<SubmissionDraft>;
    if (typeof parsed?.submissionId !== 'string') return null;
    return { ...emptyDraft(parsed.submissionId), ...parsed };
  } catch {
    return null;
  }
}

export function saveDraft(modelId: string, draft: SubmissionDraft): void {
  localStorage.setItem(draftKey(modelId), JSON.stringify(draft));
}

export function clearDraft(modelId: string): void {
  localStorage.removeItem(draftKey(modelId));
}
