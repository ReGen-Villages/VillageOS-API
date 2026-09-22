import { describe, it, expect } from 'vitest';
import { parseSolidMesh } from './geometryDispatcher';

// ── Test fixture: a simple box in y-up Three.js coords ──────────────

/** 2×2×3 box centred at (5, 0, -10), height 3m, y-up. */
function makeBoxMeshData() {
  const positions = [
    4, 0, -11,  6, 0, -11,  6, 0, -9,
    4, 0, -11,  6, 0, -9,   4, 0, -9,
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
  };
}

describe('parseSolidMesh', () => {
  it('converts arrays to typed arrays', () => {
    const data = makeBoxMeshData();
    const mesh = parseSolidMesh(data);
    expect(mesh).not.toBeNull();
    expect(mesh!.positions).toBeInstanceOf(Float32Array);
    expect(mesh!.indices).toBeInstanceOf(Uint16Array);
    expect(mesh!.normals).toBeInstanceOf(Float32Array);
    expect(mesh!.positions.length).toBe(36);
    expect(mesh!.indices.length).toBe(12);
    expect(mesh!.height).toBe(3);
    expect(mesh!.localCenter).toEqual([5, -10]);
  });

  it('returns null for empty positions', () => {
    const data = { ...makeBoxMeshData(), positions: [] };
    expect(parseSolidMesh(data)).toBeNull();
  });

  it('returns null for empty indices', () => {
    const data = { ...makeBoxMeshData(), indices: [] };
    expect(parseSolidMesh(data)).toBeNull();
  });

  it('returns null for invalid data', () => {
    expect(parseSolidMesh(null)).toBeNull();
    expect(parseSolidMesh({})).toBeNull();
    expect(parseSolidMesh(42)).toBeNull();
  });
});
