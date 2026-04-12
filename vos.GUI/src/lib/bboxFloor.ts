import { LngLatBounds } from 'maplibre-gl';

/**
 * Minimum bbox span (in degrees) we will let MapLibre fitBounds collapse to.
 * 0.005 deg ≈ 500 m at mid-latitudes ≈ MapLibre zoom 16, where Carto vector
 * tiles still exist. Without this floor, fitting a bbox of 1-2 nodes at
 * near-identical coordinates pushes MapLibre to zoom 22 and the user sees a
 * solid background because no vector tiles exist at that zoom. Regression
 * guard for Bug #5165.
 */
export const MIN_BBOX_SPAN_DEG = 0.005;

/**
 * If the given bounds span less than {@link MIN_BBOX_SPAN_DEG} on either axis,
 * return a new bounds expanded around the original center to that minimum.
 * Idempotent for already-large bounds.
 */
export function enlargeDegenerateBounds(bounds: LngLatBounds): LngLatBounds {
  const ne = bounds.getNorthEast();
  const sw = bounds.getSouthWest();
  const lngSpan = ne.lng - sw.lng;
  const latSpan = ne.lat - sw.lat;
  if (lngSpan >= MIN_BBOX_SPAN_DEG && latSpan >= MIN_BBOX_SPAN_DEG) return bounds;
  const center = bounds.getCenter();
  const half = MIN_BBOX_SPAN_DEG / 2;
  return new LngLatBounds(
    [Math.min(sw.lng, center.lng - half), Math.min(sw.lat, center.lat - half)],
    [Math.max(ne.lng, center.lng + half), Math.max(ne.lat, center.lat + half)],
  );
}
