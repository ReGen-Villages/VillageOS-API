/**
 * What the plot-first explore page holds, and everything that can be worked out from it without a
 * screen. The page inverts the guided wizard: the land comes first — a pin, then a boundary fetched
 * from a land register or drawn — and everything a person would have typed is a default the model
 * supplied or a dial on the report. What is posted is the same submission document the wizard posts, to
 * the same service, under the same identifier for the life of the exploration — a dial moving is the
 * document going again, and the model updating what it already holds.
 */

import type { FormOptions } from '../api/intakeApi';
import type {
  ProgrammeShares,
  ReportedHazards,
  SubmissionDocument,
} from '../intake/submissionDraft';
import { enclosesLand, type BoundaryPoint } from '../utils/parcelGeometry';

/** The term the land-intake template declares for a boundary a register answered. The other two ways a
 *  boundary comes to be here reuse the wizard's terms. */
export const BOUNDARY_FETCHED_FROM_REGISTER = 'fetched-from-register';

export type ExploreBoundarySource = typeof BOUNDARY_FETCHED_FROM_REGISTER | 'drawn-by-hand';

export interface ExploreState {
  /** Minted when the exploration starts and kept for its whole life, so every re-post lands on the
   *  Things the first post minted — which is the entire mechanism behind the dials. */
  readonly submissionId: string;
  readonly position: BoundaryPoint | null;
  readonly boundary: readonly BoundaryPoint[];
  readonly boundarySource: ExploreBoundarySource | null;
  /** The register's credit line, displayed beside the boundary it answered. */
  readonly boundaryAttribution: string | null;
  readonly siteName: string;
  readonly contactName: string;
  readonly emailAddress: string;
  /** Dials, kept as typed: parsing happens on the way out, where a half-typed figure is absent rather
   *  than zero. Empty is a real state — the report leads with what the land could support, and these
   *  stay unset until the person turns them. */
  readonly population: string;
  readonly householdSize: string;
  readonly shares: ProgrammeShares;
  readonly reportedHazards: ReportedHazards;
}

export function emptyExplore(submissionId: string): ExploreState {
  return {
    submissionId,
    position: null,
    boundary: [],
    boundarySource: null,
    boundaryAttribution: null,
    siteName: '',
    contactName: '',
    emailAddress: '',
    population: '',
    householdSize: '',
    shares: {},
    reportedHazards: {},
  };
}

/** The full starting split the model offers, one share per category that states a default. The page
 *  always posts the whole set with explicit values, because an allocation absent from a later post
 *  keeps the share it had — posting all of them is what makes a slider at zero mean zero. */
export function seededShares(options: FormOptions): ProgrammeShares {
  return Object.fromEntries(
    options.defaultProgramme.map((share) => [share.category, share.sharePct]),
  );
}

/** A new pin starts the land over: whatever boundary was fetched or drawn described the old position.
 *  The site's name follows the pin until somebody edits it, which `named` below is for. */
export function withPosition(state: ExploreState, position: BoundaryPoint): ExploreState {
  return {
    ...state,
    position,
    boundary: [],
    boundarySource: null,
    boundaryAttribution: null,
    siteName: defaultSiteName(position),
  };
}

/** What the land is called until somebody says otherwise. The position spelled out, because it is the
 *  one thing genuinely known — a made-up word would read as an answer where this reads as a default. */
export function defaultSiteName(position: BoundaryPoint): string {
  return `Site at ${position.latitude.toFixed(4)}, ${position.longitude.toFixed(4)}`;
}

export function withFetchedBoundary(
  state: ExploreState, boundary: readonly BoundaryPoint[], attribution: string | null,
): ExploreState {
  return {
    ...state,
    boundary,
    boundarySource: BOUNDARY_FETCHED_FROM_REGISTER,
    boundaryAttribution: attribution,
  };
}

/** A corner placed or moved by hand makes the whole boundary the person's own assertion, exactly as it
 *  does in the wizard — a dragged register boundary must not go on reading as the register's. */
export function withDrawnBoundary(
  state: ExploreState, boundary: readonly BoundaryPoint[],
): ExploreState {
  return { ...state, boundary, boundarySource: 'drawn-by-hand', boundaryAttribution: null };
}

export function withBoundaryCleared(state: ExploreState): ExploreState {
  return { ...state, boundary: [], boundarySource: null, boundaryAttribution: null };
}

/** Whether the land is described: a position, and a boundary that encloses some. This is what gates the
 *  step where a name and a mailbox are asked for — nothing about a person is wanted before there is
 *  land to ask about. */
export function landDescribed(state: ExploreState): boolean {
  return state.position !== null && enclosesLand(state.boundary);
}

export function readyToSubmit(state: ExploreState): boolean {
  return (
    landDescribed(state)
    && [state.siteName, state.contactName, state.emailAddress].every(
      (given) => given.trim().length > 0,
    )
  );
}

/**
 * The document the intake service takes — the same one the wizard posts. The project is minted from the
 * site's name because a submission is one undertaking and this page asked for no second name; a person
 * who goes on with it renames the project where projects are worked on.
 */
export function documentFrom(state: ExploreState): SubmissionDocument {
  const allocations = Object.entries(state.shares).map(([category, sharePct]) => ({
    category,
    sharePct,
  }));
  const hazards = Object.entries(state.reportedHazards).map(([hazardType, reportedLevel]) => ({
    hazardType,
    reportedLevel,
  }));
  const population = wholeNumberFrom(state.population);
  const householdSize = numberFrom(state.householdSize);

  return {
    submissionId: state.submissionId,
    project: { name: `${state.siteName.trim()} exploration` },
    contact: { name: state.contactName.trim(), emailAddress: state.emailAddress.trim() },
    site: {
      name: state.siteName.trim(),
      ...(state.position ? { latitude: state.position.latitude, longitude: state.position.longitude } : {}),
      ...(population !== null ? { population } : {}),
      ...(householdSize !== null ? { householdSize } : {}),
    },
    ...(state.boundarySource !== null && state.boundary.length > 0
      ? { parcel: { boundarySource: state.boundarySource, boundary: [...state.boundary] } }
      : {}),
    ...(allocations.length > 0 ? { allocations } : {}),
    ...(hazards.length > 0 ? { hazards } : {}),
  };
}

function numberFrom(typed: string): number | null {
  const text = typed.trim();
  if (text.length === 0) return null;
  const value = Number(text);
  return Number.isFinite(value) && value >= 0 ? value : null;
}

function wholeNumberFrom(typed: string): number | null {
  const value = numberFrom(typed);
  return value === null ? null : Math.round(value);
}
