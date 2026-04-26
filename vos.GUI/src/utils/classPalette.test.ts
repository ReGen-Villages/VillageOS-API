import { describe, it, expect } from 'vitest';
import {
  BUCKET_COLORS,
  getClassBucket,
  resolveClassColor,
  type ClassBucket,
} from './classPalette';

describe('getClassBucket (Feature #5340 — generic, property-name-keyed)', () => {
  describe('IFC default table (registered under propertyName="ifcClass")', () => {
    it.each<[string, ClassBucket]>([
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
      expect(getClassBucket('ifcClass', cls)).toBe(bucket);
    });
  });

  describe('IFC suffix rule (typeDef)', () => {
    it.each([
      'IfcWallType',
      'IfcDoorType',
      'IfcSlabType',
      'IfcDistributionPortType',
      'IfcCoveringType',
      'IfcFurnishingElementType',
    ])('routes %s to typeDef via suffix match', (cls) => {
      expect(getClassBucket('ifcClass', cls)).toBe('typeDef');
    });

    it('does not match arbitrary *Type strings without the Ifc prefix', () => {
      expect(getClassBucket('ifcClass', 'SomeRandomType')).toBe('other');
      expect(getClassBucket('ifcClass', 'Type')).toBe('other');
    });
  });

  describe('property-name agnostic — non-IFC properties', () => {
    it('returns "other" when no table is registered for the property', () => {
      expect(getClassBucket('kind', 'building')).toBe('other');
      expect(getClassBucket('category', 'spatial')).toBe('other');
      expect(getClassBucket('myCustomProperty', 'foo')).toBe('other');
    });

    it('does NOT apply the IFC suffix rule to other property names', () => {
      // Suffix rules are scoped per propertyName — IfcWallType only buckets
      // when read from `ifcClass`, not from `kind`.
      expect(getClassBucket('kind', 'IfcWallType')).toBe('other');
    });
  });

  describe('null / undefined / empty', () => {
    it.each([null, undefined, ''])('returns other for %s', (input) => {
      expect(getClassBucket('ifcClass', input as string | null | undefined)).toBe('other');
    });
  });
});

describe('BUCKET_COLORS (Feature #5340)', () => {
  it('defines a hex color for every bucket', () => {
    const buckets: ClassBucket[] = [
      'spatial', 'structural', 'opening', 'mep', 'port',
      'finish', 'site', 'material', 'typeDef', 'other',
    ];
    for (const b of buckets) {
      expect(BUCKET_COLORS[b]).toMatch(/^#[0-9a-f]{6}$/i);
    }
  });

  it('every bucket color is unique (no two buckets share a hex)', () => {
    const values = Object.values(BUCKET_COLORS);
    expect(new Set(values).size).toBe(values.length);
  });
});

describe('resolveClassColor (Feature #5340 — override priority)', () => {
  it('returns the bucket color when no override is supplied (IFC default)', () => {
    expect(resolveClassColor('ifcClass', 'IfcWall')).toBe(BUCKET_COLORS.structural);
    expect(resolveClassColor('ifcClass', 'IfcDistributionPort')).toBe(BUCKET_COLORS.port);
  });

  it('returns the other bucket color for unknown / null / non-registered property', () => {
    expect(resolveClassColor('ifcClass', null)).toBe(BUCKET_COLORS.other);
    expect(resolveClassColor('ifcClass', undefined)).toBe(BUCKET_COLORS.other);
    expect(resolveClassColor('ifcClass', 'Bogus')).toBe(BUCKET_COLORS.other);
    // Unknown property — no table registered, no suffix rule
    expect(resolveClassColor('myProp', 'anything')).toBe(BUCKET_COLORS.other);
  });

  it('user override beats the curated bucket color (extension point)', () => {
    expect(resolveClassColor('ifcClass', 'IfcWall', { IfcWall: '#ff00ff' })).toBe('#ff00ff');
  });

  it('override only applies to the matching value', () => {
    const overrides = { IfcWall: '#ff00ff' };
    expect(resolveClassColor('ifcClass', 'IfcSlab', overrides)).toBe(BUCKET_COLORS.structural);
  });

  it('override works for ANY propertyName (not just registered ones)', () => {
    // A non-IFC deployment using `category` → "Hub" with a user override gets
    // the override — no need to register a bucket table first.
    expect(resolveClassColor('category', 'Hub', { Hub: '#abc123' })).toBe('#abc123');
  });

  it('null/undefined value with overrides still returns other (no crash)', () => {
    expect(resolveClassColor('ifcClass', null, { IfcWall: '#ff00ff' })).toBe(BUCKET_COLORS.other);
  });
});
