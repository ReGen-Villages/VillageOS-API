import { describe, it, expect } from 'vitest';
import { prepareVillageScene } from './villageScene';
import type { VosThing, VosRelationship } from '../types/vos';

/**
 * Create IFC mesh data for a simple box.
 * positions/indices/normals are minimal but valid for parsing.
 */
function makeIfcMeshData(cx: number, cz: number, h: number) {
  // Two-triangle quad (floor) — minimal valid mesh
  const x0 = cx - 5, x1 = cx + 5;
  const z0 = cz - 5, z1 = cz + 5;
  return {
    positions: [
      x0, 0, z0,  x1, 0, z0,  x1, 0, z1,
      x0, 0, z0,  x1, 0, z1,  x0, 0, z1,
    ],
    indices: [0, 1, 2, 3, 4, 5],
    normals: [
      0, 1, 0,  0, 1, 0,  0, 1, 0,
      0, 1, 0,  0, 1, 0,  0, 1, 0,
    ],
    height: h,
    localCenter: [cx, cz],
    centroid: { lat: 40.7937, lng: -73.6612 },
  };
}

function makeThing(id: string, name: string, geometry?: unknown): VosThing {
  return {
    Id: id,
    Name: name,
    Properties: geometry ? { geometry } : {},
  };
}

describe('prepareVillageScene', () => {
  it('returns null for empty thing list', () => {
    expect(prepareVillageScene([])).toBeNull();
  });

  it('returns null when no things have geometry', () => {
    const things = [
      makeThing('1', 'Sensor-1'),
      makeThing('2', 'Controller-1'),
    ];
    expect(prepareVillageScene(things)).toBeNull();
  });

  it('extracts buildings from things with geometry', () => {
    const things = [
      makeThing('1', 'Home-1', makeIfcMeshData(0, 0, 4)),
      makeThing('2', 'Sensor-1'), // no geometry
      makeThing('3', 'Home-2', makeIfcMeshData(50, 50, 4.5)),
    ];
    const scene = prepareVillageScene(things);
    expect(scene).not.toBeNull();
    expect(scene!.buildings).toHaveLength(2);
    expect(scene!.buildings[0].thingName).toBe('Home-1');
    expect(scene!.buildings[1].thingName).toBe('Home-2');
  });

  it('computes bounds across all buildings', () => {
    const things = [
      makeThing('1', 'Home-1', makeIfcMeshData(0, 0, 4)),
      makeThing('2', 'Home-2', makeIfcMeshData(100, 50, 5)),
    ];
    const scene = prepareVillageScene(things);
    expect(scene!.bounds.maxHeight).toBeCloseTo(5, 0);
    expect(scene!.bounds.maxX).toBeGreaterThan(50);
  });

  it('uses colorMap when provided', () => {
    const things = [
      makeThing('1', 'Home-1', makeIfcMeshData(0, 0, 4)),
    ];
    const colorMap = new Map([['1', '#ff0000']]);
    const scene = prepareVillageScene(things, colorMap);
    expect(scene!.buildings[0].color).toBe('#ff0000');
  });

  it('falls back to name-based color when no colorMap', () => {
    const things = [
      makeThing('1', 'Home-1', makeIfcMeshData(0, 0, 4)),
    ];
    const scene = prepareVillageScene(things);
    expect(scene!.buildings[0].color).toMatch(/^#[0-9a-f]{6}$/);
  });

  it('computes maxHeight correctly when later building is shorter', () => {
    const things = [
      makeThing('1', 'Tall', makeIfcMeshData(0, 0, 8)),
      makeThing('2', 'Short', makeIfcMeshData(50, 0, 3)),
    ];
    const scene = prepareVillageScene(things);
    expect(scene!.bounds.maxHeight).toBeCloseTo(8, 0);
  });

  it('skips things with invalid geometry (parseSolidMesh returns null)', () => {
    const things = [
      makeThing('1', 'Good', makeIfcMeshData(0, 0, 5)),
      makeThing('2', 'Bad', 'not valid json'),
    ];
    const scene = prepareVillageScene(things);
    expect(scene).not.toBeNull();
    expect(scene!.buildings).toHaveLength(1);
    expect(scene!.buildings[0].thingName).toBe('Good');
  });

  it('includes child meshes via contains relationships', () => {
    const containsPred = makeThing('pred-contains', 'contains');
    const building: VosThing = {
      Id: 'building-1',
      Name: 'Building-A',
      Properties: { ifcClass: 'IfcBuilding' },
    };
    const wall = makeThing('wall-1', 'Wall-1', makeIfcMeshData(0, 0, 3));

    const relationships: VosRelationship[] = [
      {
        Id: 'rel-1', Name: 'Building-A contains Wall-1',
        SubjectId: 'building-1', PredicateId: 'pred-contains', TargetId: 'wall-1',
        Properties: {},
      },
    ];

    const scene = prepareVillageScene(
      [containsPred, building, wall],
      undefined,
      relationships,
    );
    expect(scene).not.toBeNull();
    expect(scene!.buildings).toHaveLength(1);
    expect(scene!.buildings[0].thingName).toBe('Wall-1');
  });

  it('does not duplicate children already added as top-level', () => {
    const containsPred = makeThing('pred-contains', 'contains');
    const building: VosThing = {
      Id: 'building-1',
      Name: 'Building-A',
      Properties: { ifcClass: 'IfcBuilding' },
    };
    const wall = makeThing('wall-1', 'Wall-1', makeIfcMeshData(0, 0, 3));

    const relationships: VosRelationship[] = [
      {
        Id: 'rel-1', Name: 'Building-A contains Wall-1',
        SubjectId: 'building-1', PredicateId: 'pred-contains', TargetId: 'wall-1',
        Properties: {},
      },
    ];

    const scene = prepareVillageScene(
      [containsPred, wall, building],
      undefined,
      relationships,
    );
    expect(scene).not.toBeNull();
    expect(scene!.buildings).toHaveLength(1);
  });

  it('works without relationships parameter (backward compatible)', () => {
    const things = [
      makeThing('1', 'Home-1', makeIfcMeshData(0, 0, 4)),
    ];
    const scene = prepareVillageScene(things);
    expect(scene).not.toBeNull();
    expect(scene!.buildings).toHaveLength(1);
  });
});
