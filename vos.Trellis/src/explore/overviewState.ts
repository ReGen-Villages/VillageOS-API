/**
 * What the closing view is made of, worked out without a screen: the sections the submitted page
 * declares as tabs, and the basemap the view opens on.
 */
import type { BasemapSource } from '../types/basemap';
import type { DashboardSection, DashboardSpecification } from '../types/dashboard';

/** The sections carrying a `tab`, in the order the spec lists them — the order the sheet draws them. */
export function tabSections(specification: Pick<DashboardSpecification, 'sections'>): DashboardSection[] {
  return specification.sections.filter((section) => section.tab !== undefined);
}

/** The basemap the view opens on: the first source drawing a tile pyramid, which is how imagery is
 *  served, since the view is the parcel on the photograph of its land. A model declaring only styled
 *  maps opens on whichever the map would have shown anyway. */
export function imagerySource(sources: readonly BasemapSource[]): BasemapSource | null {
  return sources.find((source) => source.kind === 'raster') ?? null;
}
