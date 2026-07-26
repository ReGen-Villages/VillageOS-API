import { describe, it, expect } from 'vitest';
import { searchProperties } from './propertySearch';
import type { EffectiveProperty, VosRelationship } from '../types/vos';

const ep = (value: unknown, isInherited: boolean, inheritedFrom?: string): EffectiveProperty => ({
  Value: value,
  Type: 'vos.String',
  IsInherited: isInherited,
  InheritedFrom: inheritedFrom,
});

const HOME_ID = '11111111-1111-1111-1111-111111111111';
const INSTANCE_ID = '22222222-2222-2222-2222-222222222222';

const thingNames = new Map<string, string>([
  [HOME_ID, 'Home'],
  [INSTANCE_ID, 'Serpentine-Home-4'],
]);

describe('searchProperties', () => {
  it('surfaces an inherited property the instance never overrode (regression for #5909)', () => {
    // The server keys inherited properties by qualified path and tags them IsInherited with the source.
    // Before the fix, search walked the client override-only tree, which does not contain this value.
    const effectiveProps = {
      [INSTANCE_ID]: {
        'Home.energy_rating': ep('A+', true, HOME_ID),
      },
    };

    const results = searchProperties({
      effectiveProps, relationships: [], thingNames, query: 'energy_rating', mode: 'name',
    });

    expect(results).toHaveLength(1);
    expect(results[0]).toMatchObject({
      propertyName: 'energy_rating',   // leaf, not the qualified path
      value: 'A+',
      ownerType: 'thing',
      ownerId: INSTANCE_ID,
      ownerName: 'Serpentine-Home-4',
      inheritedFrom: 'Home',           // source guid resolved to name for the "via" badge
    });
  });

  it('surfaces own properties with no inheritance source', () => {
    const effectiveProps = { [INSTANCE_ID]: { status: ep('active', false) } };

    const results = searchProperties({
      effectiveProps, relationships: [], thingNames, query: 'status', mode: 'name',
    });

    expect(results).toHaveLength(1);
    expect(results[0].propertyName).toBe('status');
    expect(results[0].inheritedFrom).toBeUndefined();
  });

  it('matches by value when mode is value', () => {
    const effectiveProps = {
      [INSTANCE_ID]: { status: ep('active', false), 'Home.energy_rating': ep('A+', true, HOME_ID) },
    };

    const results = searchProperties({
      effectiveProps, relationships: [], thingNames, query: 'a+', mode: 'value',
    });

    expect(results.map((r) => r.propertyName)).toEqual(['energy_rating']);
  });

  it('skips bulky blob property names', () => {
    const effectiveProps = { [INSTANCE_ID]: { geometry: ep('<mesh>', false) } };

    const results = searchProperties({
      effectiveProps, relationships: [], thingNames, query: 'geometry', mode: 'name',
    });

    expect(results).toHaveLength(0);
  });

  it('searches relationship properties from the live store', () => {
    const rel: VosRelationship = {
      Id: 'rel-1', Name: 'consumes', SubjectId: INSTANCE_ID, PredicateId: 'p', TargetId: HOME_ID,
      Properties: { rate: 5 },
    };

    const results = searchProperties({
      effectiveProps: {}, relationships: [rel], thingNames, query: 'rate', mode: 'name',
    });

    expect(results).toHaveLength(1);
    expect(results[0]).toMatchObject({ ownerType: 'relationship', propertyName: 'rate', value: 5 });
  });

  it('returns nothing while the effective snapshot is still loading', () => {
    const results = searchProperties({
      effectiveProps: null, relationships: [], thingNames, query: 'anything', mode: 'name',
    });

    expect(results).toEqual([]);
  });
});
