import { describe, it, expect } from 'vitest';
import {
  BUCKET_COLORS,
  getIfcClassBucket,
  resolveIfcClassColor,
  type IfcClassBucket,
} from './ifcClassPalette';

describe('getIfcClassBucket (Feature #5340)', () => {
  describe('explicit bucket assignments', () => {
    it.each<[string, IfcClassBucket]>([
      ['IfcSite', 'spatial'],
      ['IfcBuilding', 'spatial'],
      ['IfcBuildingStorey', 'spatial'],
      ['IfcSpace', 'spatial'],
      ['IfcWall', 'structural'],
      ['IfcSlab', 'structural'],
      ['IfcBeam', 'structural'],
      ['IfcColumn', 'structural'],
      ['IfcDoor', 'opening'],
      ['IfcWindow', 'opening'],
      ['IfcOpeningElement', 'opening'],
      ['IfcDistributionFlowElement', 'mep'],
      ['IfcFlowController', 'mep'],
      ['IfcDistributionPort', 'port'],
      ['IfcCovering', 'finish'],
      ['IfcFurnishingElement', 'finish'],
      ['IfcGeographicElement', 'site'],
      ['IfcMaterial', 'material'],
      ['IfcMaterialLayer', 'material'],
      ['IfcMaterialLayerSet', 'material'],
      ['IfcMaterialConstituent', 'material'],
      ['IfcMaterialProfile', 'material'],
      ['IfcMaterialLayerSetUsage', 'material'],
    ])('routes %s to bucket %s', (cls, bucket) => {
      expect(getIfcClassBucket(cls)).toBe(bucket);
    });
  });

  describe('*Type suffix detection', () => {
    it.each([
      'IfcWallType',
      'IfcDoorType',
      'IfcSlabType',
      'IfcDistributionPortType',
      'IfcCoveringType',
      'IfcFurnishingElementType',
    ])('routes %s to typeDef via suffix match', (cls) => {
      expect(getIfcClassBucket(cls)).toBe('typeDef');
    });

    it('does not match non-Ifc *Type strings', () => {
      // Guards against an over-broad endsWith('Type') that could mis-bucket
      // arbitrary user property names.
      expect(getIfcClassBucket('SomeRandomType')).toBe('other');
      expect(getIfcClassBucket('Type')).toBe('other');
    });
  });

  describe('fallback to other', () => {
    it.each([null, undefined, '', 'IfcUnknownClass', 'NotAnIfcThing'])(
      'returns other for %s',
      (input) => {
        expect(getIfcClassBucket(input as string | null | undefined)).toBe('other');
      },
    );
  });
});

describe('BUCKET_COLORS (Feature #5340)', () => {
  it('defines a hex color for every bucket', () => {
    const buckets: IfcClassBucket[] = [
      'spatial', 'structural', 'opening', 'mep', 'port',
      'finish', 'site', 'material', 'typeDef', 'other',
    ];
    for (const b of buckets) {
      expect(BUCKET_COLORS[b]).toMatch(/^#[0-9a-f]{6}$/i);
    }
  });

  it('every bucket color is unique (no two buckets share a hex)', () => {
    // Different buckets must be visually distinguishable; a duplicate would
    // silently merge two semantic groups on the canvas.
    const values = Object.values(BUCKET_COLORS);
    expect(new Set(values).size).toBe(values.length);
  });
});

describe('resolveIfcClassColor (Feature #5340 — override priority)', () => {
  it('returns the bucket color when no override is supplied', () => {
    expect(resolveIfcClassColor('IfcWall')).toBe(BUCKET_COLORS.structural);
    expect(resolveIfcClassColor('IfcDistributionPort')).toBe(BUCKET_COLORS.port);
  });

  it('returns the other bucket color for unknown / null input', () => {
    expect(resolveIfcClassColor(null)).toBe(BUCKET_COLORS.other);
    expect(resolveIfcClassColor(undefined)).toBe(BUCKET_COLORS.other);
    expect(resolveIfcClassColor('Bogus')).toBe(BUCKET_COLORS.other);
  });

  it('user override beats the curated bucket color (extension point)', () => {
    // This is the hook the future GUI_Settings.IfcClassColors panel will use.
    expect(resolveIfcClassColor('IfcWall', { IfcWall: '#ff00ff' })).toBe('#ff00ff');
  });

  it('override only applies to the matching class', () => {
    const overrides = { IfcWall: '#ff00ff' };
    expect(resolveIfcClassColor('IfcSlab', overrides)).toBe(BUCKET_COLORS.structural);
  });

  it('null/undefined ifcClass with overrides still returns other (no crash)', () => {
    expect(resolveIfcClassColor(null, { IfcWall: '#ff00ff' })).toBe(BUCKET_COLORS.other);
  });

  it('round-trip: bucket color is reachable for every explicit class', () => {
    // Realistic sample drawn from the MV jehrlich ingest. Asserts the wiring
    // explicit class -> bucket -> hex never silently drops to "other".
    const sample = ['IfcWall', 'IfcSlab', 'IfcDoor', 'IfcWindow',
      'IfcDistributionPort', 'IfcCovering', 'IfcMaterial',
      'IfcMaterialLayerSet', 'IfcSite', 'IfcBuilding'];
    for (const cls of sample) {
      const bucket = getIfcClassBucket(cls);
      expect(bucket).not.toBe('other');
      expect(resolveIfcClassColor(cls)).toBe(BUCKET_COLORS[bucket]);
    }
  });
});
