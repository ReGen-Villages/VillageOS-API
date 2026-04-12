import { LngLatBounds } from 'maplibre-gl';
import { describe, it, expect } from 'vitest';
import { enlargeDegenerateBounds, MIN_BBOX_SPAN_DEG } from './bboxFloor';

describe('enlargeDegenerateBounds', () => {
  it('passes through bounds already larger than the minimum span', () => {
    const bounds = new LngLatBounds([-70.6, 41.4], [-70.4, 41.5]); // 0.2 deg × 0.1 deg
    const result = enlargeDegenerateBounds(bounds);
    expect(result).toBe(bounds);
  });

  it('expands a zero-size bbox to MIN_BBOX_SPAN_DEG around center', () => {
    const lng = -70.5;
    const lat = 41.43;
    const bounds = new LngLatBounds([lng, lat], [lng, lat]);
    const result = enlargeDegenerateBounds(bounds);

    const ne = result.getNorthEast();
    const sw = result.getSouthWest();
    const lngSpan = ne.lng - sw.lng;
    const latSpan = ne.lat - sw.lat;

    expect(lngSpan).toBeGreaterThanOrEqual(MIN_BBOX_SPAN_DEG - 1e-9);
    expect(latSpan).toBeGreaterThanOrEqual(MIN_BBOX_SPAN_DEG - 1e-9);
    // Center should be preserved
    const resultCenter = result.getCenter();
    expect(resultCenter.lng).toBeCloseTo(lng, 6);
    expect(resultCenter.lat).toBeCloseTo(lat, 6);
  });

  it('expands when only longitude span is below the floor', () => {
    // Long but very narrow in longitude
    const bounds = new LngLatBounds([-70.5, 41.0], [-70.5, 41.5]);
    const result = enlargeDegenerateBounds(bounds);
    const ne = result.getNorthEast();
    const sw = result.getSouthWest();
    expect(ne.lng - sw.lng).toBeGreaterThanOrEqual(MIN_BBOX_SPAN_DEG - 1e-9);
    // Latitude should not shrink — it was already large enough
    expect(ne.lat - sw.lat).toBeGreaterThanOrEqual(0.5);
  });

  it('expands when only latitude span is below the floor', () => {
    const bounds = new LngLatBounds([-71.0, 41.43], [-70.5, 41.43]);
    const result = enlargeDegenerateBounds(bounds);
    const ne = result.getNorthEast();
    const sw = result.getSouthWest();
    expect(ne.lat - sw.lat).toBeGreaterThanOrEqual(MIN_BBOX_SPAN_DEG - 1e-9);
    // Longitude should not shrink — it was already large enough
    expect(ne.lng - sw.lng).toBeGreaterThanOrEqual(0.5);
  });

  it('is idempotent on the Martha\'s Vineyard primary-surface degenerate case', () => {
    // The actual failure case from Bug #5165: two nodes at the same point.
    const mvLng = -70.58744126831178;
    const mvLat = 41.43506237443077;
    const bounds = new LngLatBounds([mvLng, mvLat], [mvLng, mvLat]);
    const once = enlargeDegenerateBounds(bounds);
    const twice = enlargeDegenerateBounds(once);

    const ne1 = once.getNorthEast();
    const sw1 = once.getSouthWest();
    const ne2 = twice.getNorthEast();
    const sw2 = twice.getSouthWest();

    expect(ne2.lng).toBeCloseTo(ne1.lng, 9);
    expect(ne2.lat).toBeCloseTo(ne1.lat, 9);
    expect(sw2.lng).toBeCloseTo(sw1.lng, 9);
    expect(sw2.lat).toBeCloseTo(sw1.lat, 9);
  });
});
