import { describe, it, expect } from 'vitest';
import { parseIfcCentroid, parseIfcFootprint, parseIfcSolidMesh } from './ifcMeshParser';

// ── Test fixture: a simple box in y-up Three.js coords ──────────────

/** 2×2×3 box centred at (5, 0, -10), height 3m, y-up. */
function makeBoxMeshData() {
  // 8 corners of a box: x ∈ [4,6], y ∈ [0,3], z ∈ [-11,-9]
  const positions = [
    // Bottom face (y=0), two triangles
    4, 0, -11,  6, 0, -11,  6, 0, -9,
    4, 0, -11,  6, 0, -9,   4, 0, -9,
    // Top face (y=3), two triangles
    4, 3, -11,  6, 3, -9,   6, 3, -11,
    4, 3, -11,  4, 3, -9,   6, 3, -9,
  ];
  const normals = [
    0, -1, 0,  0, -1, 0,  0, -1, 0,
    0, -1, 0,  0, -1, 0,  0, -1, 0,
    0, 1, 0,   0, 1, 0,   0, 1, 0,
    0, 1, 0,   0, 1, 0,   0, 1, 0,
  ];
  const indices = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];

  return {
    positions,
    indices,
    normals,
    height: 3,
    localCenter: [5, -10] as [number, number],
    centroid: { lat: 40.7937, lng: -73.6612 },
    footprint: {
      type: 'Polygon' as const,
      coordinates: [[[-73.6613, 40.7936], [-73.6611, 40.7936], [-73.6611, 40.7938], [-73.6613, 40.7938], [-73.6613, 40.7936]]],
    },
  };
}

// ── parseIfcCentroid ──────────────────────────────────────────────────

describe('parseIfcCentroid', () => {
  it('extracts centroid from valid data', () => {
    const data = makeBoxMeshData();
    const result = parseIfcCentroid(data);
    expect(result).not.toBeNull();
    expect(result!.lat).toBe(40.7937);
    expect(result!.lng).toBe(-73.6612);
  });

  it('returns null when centroid is absent', () => {
    const data = { ...makeBoxMeshData(), centroid: undefined };
    expect(parseIfcCentroid(data)).toBeNull();
  });

  it('returns null for invalid data', () => {
    expect(parseIfcCentroid(null)).toBeNull();
    expect(parseIfcCentroid({})).toBeNull();
    expect(parseIfcCentroid('string')).toBeNull();
  });
});

// ── parseIfcFootprint ────────────────────────────────────────────────

describe('parseIfcFootprint', () => {
  it('extracts footprint feature from valid data', () => {
    const data = makeBoxMeshData();
    const feature = parseIfcFootprint(data, 'Wall-001', '#ff0000');
    expect(feature).not.toBeNull();
    expect(feature!.type).toBe('Feature');
    expect(feature!.properties!.name).toBe('Wall-001');
    expect(feature!.properties!.color).toBe('#ff0000');
    expect(feature!.properties!.height).toBe(3);
    expect(feature!.geometry.type).toBe('Polygon');
  });

  it('returns null when footprint is absent', () => {
    const data = { ...makeBoxMeshData(), footprint: undefined };
    expect(parseIfcFootprint(data)).toBeNull();
  });

  it('defaults name and color', () => {
    const data = makeBoxMeshData();
    const feature = parseIfcFootprint(data);
    expect(feature!.properties!.name).toBe('');
    expect(feature!.properties!.color).toBe('#6d8ea8');
  });

  it('returns null for invalid data', () => {
    expect(parseIfcFootprint(null)).toBeNull();
  });
});

// ── parseIfcSolidMesh ────────────────────────────────────────────────

describe('parseIfcSolidMesh', () => {
  it('converts arrays to typed arrays', () => {
    const data = makeBoxMeshData();
    const mesh = parseIfcSolidMesh(data);
    expect(mesh).not.toBeNull();
    expect(mesh!.positions).toBeInstanceOf(Float32Array);
    expect(mesh!.indices).toBeInstanceOf(Uint16Array);
    expect(mesh!.normals).toBeInstanceOf(Float32Array);
    expect(mesh!.positions.length).toBe(36); // 12 vertices × 3 coords
    expect(mesh!.indices.length).toBe(12);   // 4 triangles × 3
    expect(mesh!.height).toBe(3);
    expect(mesh!.localCenter).toEqual([5, -10]);
  });

  it('returns null for empty positions', () => {
    const data = { ...makeBoxMeshData(), positions: [] };
    expect(parseIfcSolidMesh(data)).toBeNull();
  });

  it('returns null for empty indices', () => {
    const data = { ...makeBoxMeshData(), indices: [] };
    expect(parseIfcSolidMesh(data)).toBeNull();
  });

  it('returns null for invalid data', () => {
    expect(parseIfcSolidMesh(null)).toBeNull();
    expect(parseIfcSolidMesh({})).toBeNull();
    expect(parseIfcSolidMesh(42)).toBeNull();
  });
});
