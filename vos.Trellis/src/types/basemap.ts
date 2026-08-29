/**
 * Generic, model-agnostic basemap contract.
 *
 * Trellis ships the map; the model supplies what it draws. A model declares one
 * or more Things of archetype `BasemapSource`, each carrying the address of a
 * vector style or a raster tile template plus the attribution its licence
 * requires. No provider is named in this file or anywhere else in the client —
 * changing which imagery a deployment shows is a change to the model.
 */

/** The archetype a model-resident basemap source Thing must be `is`-linked to. */
export const BASEMAP_SOURCE_ARCHETYPE = 'BasemapSource';
/** Address of a vector style document the map loads whole. */
export const BASEMAP_STYLE_URL_PROPERTY = 'styleUrl';
/** Address template of a raster tile pyramid, carrying `{z}`, `{x}` and `{y}`. */
export const BASEMAP_TILE_URL_PROPERTY = 'tileUrl';
/** The credit the source's licence requires the map to display. */
export const BASEMAP_ATTRIBUTION_PROPERTY = 'attribution';
/** Deepest zoom level a raster source has tiles for. */
export const BASEMAP_MAXIMUM_ZOOM_PROPERTY = 'maximumZoom';

/** A source as the model states it, before anything has judged whether it can be drawn. This is what the
 *  intake service hands a public form, which cannot read the model for itself. */
export interface DeclaredBasemapSource {
  id: string;
  name: string;
  attribution?: string | null;
  styleUrl?: string | null;
  tileUrl?: string | null;
  maximumZoom?: number | null;
}

/** A source the model holds, ready for the layer switch to offer by name. */
export type BasemapSource =
  | {
      id: string;
      name: string;
      attribution: string;
      kind: 'style';
      styleUrl: string;
    }
  | {
      id: string;
      name: string;
      attribution: string;
      kind: 'raster';
      tileUrl: string;
      maximumZoom: number;
    };

/** Where a raster source stops when the model does not say. Beyond this the map keeps the
 *  deepest tiles it has rather than requesting addresses the pyramid cannot answer. */
export const DEFAULT_RASTER_MAXIMUM_ZOOM = 19;
