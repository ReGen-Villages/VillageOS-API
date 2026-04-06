import { describe, it, expect } from 'vitest';
import { crossProduct, M_PER_DEG_LAT, FLAT_EXTRUDE_HEIGHT, POINT_MARKER_HALF } from './meshHelpers';

describe('crossProduct', () => {
  it('returns [0,0,0] for degenerate triangle (all points equal)', () => {
    const result = crossProduct(1, 2, 3, 1, 2, 3, 1, 2, 3);
    expect(result).toEqual([0, 0, 0]);
  });

  it('computes correct cross product for unit vectors along axes', () => {
    // Triangle: origin, (1,0,0), (0,1,0) → normal should point in +z
    const [nx, ny, nz] = crossProduct(0, 0, 0, 1, 0, 0, 0, 1, 0);
    expect(nx).toBe(0);
    expect(ny).toBe(0);
    expect(nz).toBe(1);
  });

  it('computes correct cross product for xz-plane triangle', () => {
    // Triangle: origin, (1,0,0), (0,0,1) → normal should point in -y
    const [nx, ny, nz] = crossProduct(0, 0, 0, 1, 0, 0, 0, 0, 1);
    expect(nx).toBe(0);
    expect(ny).toBe(-1);
    expect(nz).toBe(0);
  });

  it('handles non-origin triangles', () => {
    // Shifted triangle: (1,1,1), (2,1,1), (1,2,1) → same as origin case, normal +z
    const [nx, ny, nz] = crossProduct(1, 1, 1, 2, 1, 1, 1, 2, 1);
    expect(nx).toBe(0);
    expect(ny).toBe(0);
    expect(nz).toBe(1);
  });

  it('returns a tuple of 3 numbers', () => {
    const result = crossProduct(0, 0, 0, 3, 0, 0, 0, 4, 0);
    expect(result).toHaveLength(3);
    expect(result.every((v) => typeof v === 'number')).toBe(true);
  });

  it('magnitude equals area of parallelogram', () => {
    // (2,0,0) and (0,3,0) → cross magnitude = 6
    const [nx, ny, nz] = crossProduct(0, 0, 0, 2, 0, 0, 0, 3, 0);
    const mag = Math.sqrt(nx * nx + ny * ny + nz * nz);
    expect(mag).toBeCloseTo(6, 10);
  });
});

describe('constants', () => {
  it('M_PER_DEG_LAT is approximately 111320', () => {
    expect(M_PER_DEG_LAT).toBe(111_320.0);
  });

  it('FLAT_EXTRUDE_HEIGHT is positive', () => {
    expect(FLAT_EXTRUDE_HEIGHT).toBeGreaterThan(0);
  });

  it('POINT_MARKER_HALF is positive', () => {
    expect(POINT_MARKER_HALF).toBeGreaterThan(0);
  });
});
