import type { LargestArea, RegisteredPlot } from '../api/intakeApi';
import { BOUNDARY_DRAWN_BY_HAND, type SubmissionDocument } from '../intake/submissionDraft';
import { borderAreaHectares, type AreaMethod } from '../utils/borderArea';
import { enclosesLand, type BoundaryPoint } from '../utils/parcelGeometry';
import type { StudySession } from './SignIn';
import { siteAfterImport } from './siteMove';

export type BorderSource = 'drawn' | 'imported' | 'register';

export interface LocationSite {
  readonly address: string | null;
  readonly position: BoundaryPoint;
  readonly parcelName: string | null;
  readonly border: readonly BoundaryPoint[];
  readonly borderSource: BorderSource | null;
  readonly plotLabel: string | null;
  /** The register's or the intake's own measure, which a border edited on the page no longer has. */
  readonly givenHectares: number | null;
}

export type SiteArea =
  | { readonly kind: 'register'; readonly hectares: number; readonly label: string | null }
  | { readonly kind: 'drawn' | 'imported'; readonly hectares: number }
  | { readonly kind: 'unknown' };

const BOUNDARY_SOURCE_TERMS: Readonly<Record<BorderSource, string>> = {
  drawn: BOUNDARY_DRAWN_BY_HAND,
  imported: 'imported-from-file',
  register: 'fetched-from-register',
};

const NO_BORDER = { border: [], borderSource: null, plotLabel: null, givenHectares: null } as const;

export function siteFound(address: string | null, position: BoundaryPoint): LocationSite {
  return { address, position, parcelName: null, ...NO_BORDER };
}

export function borderDrawn(site: LocationSite, border: readonly BoundaryPoint[]): LocationSite {
  return { ...site, border, borderSource: 'drawn', plotLabel: null, givenHectares: null };
}

export function borderImported(site: LocationSite | null, largest: LargestArea): LocationSite {
  return {
    ...placedFor(site, largest.boundary),
    parcelName: site?.parcelName ?? largest.name,
    border: largest.boundary,
    borderSource: 'imported',
    plotLabel: null,
    givenHectares: largest.hectares,
  };
}

export function plotChosen(site: LocationSite | null, plot: RegisteredPlot): LocationSite {
  return {
    ...placedFor(site, plot.boundary),
    border: plot.boundary,
    borderSource: 'register',
    plotLabel: plot.identifier,
    givenHectares: plot.areaHectares,
  };
}

export function borderCleared(site: LocationSite): LocationSite {
  return { ...site, ...NO_BORDER };
}

export function siteArea(site: LocationSite, method: AreaMethod | null): SiteArea {
  if (!enclosesLand(site.border)) return { kind: 'unknown' };
  if (site.borderSource === 'imported' && site.givenHectares !== null) return { kind: 'imported', hectares: site.givenHectares };
  const hectares = site.givenHectares ?? (method === null ? null : borderAreaHectares(site.border, method));
  if (hectares === null) return { kind: 'unknown' };
  return site.borderSource === 'register' ? { kind: 'register', hectares, label: site.plotLabel } : { kind: 'drawn', hectares };
}

export function siteConfirmable(site: LocationSite): boolean {
  return enclosesLand(site.border);
}

/** The demo's sign-in asks no name, so the contact is named by the address until a later step asks for one. */
export function siteDocument(site: LocationSite, session: StudySession, submissionId: string): SubmissionDocument {
  return {
    submissionId,
    project: { name: session.projectName },
    contact: { name: session.emailAddress, emailAddress: session.emailAddress },
    site: {
      name: site.parcelName ?? site.plotLabel ?? site.address ?? session.projectName,
      latitude: site.position.latitude,
      longitude: site.position.longitude,
    },
    parcel: { boundarySource: BOUNDARY_SOURCE_TERMS[site.borderSource ?? 'drawn'], boundary: [...site.border] },
  };
}

function placedFor(site: LocationSite | null, border: readonly BoundaryPoint[]): LocationSite {
  const { position } = siteAfterImport(site?.position ?? null, border);
  return site === null ? siteFound(null, position) : { ...site, position };
}
