import { describe, it, expect } from 'vitest';
import { relationshipTakesReadingsOnly, thingTakesReadingsOnly, withDeclaredTypes } from './editableProperties';
import type { EffectiveProperty, IsChainLookup, VosRelationship, VosThing } from '@tendril-ui/tendril';

const resolved =(entries: Record<string, string>): Record<string, EffectiveProperty> =>
  Object.fromEntries(
    Object.entries(entries).map(([name, Type]) => [name, { Value: null, Type, IsInherited: false }]),
  );

const writable = () => false;

// A save states the type the platform reports for the property, so a code stored as text that looks like a
// number is still saved as text.
describe('withDeclaredTypes', () => {
  it('carries the platform type alongside each value', () => {
    const paired = withDeclaredTypes(
      [['door_number', '4711'], ['open_ratio', 0.25]],
      resolved({ door_number: 'vos.String', open_ratio: 'vos.Decimal' }),
      writable,
    );
    expect(paired).toEqual([
      { name: 'door_number', value: '4711', type: 'vos.String', takesReadingsOnly: false },
      { name: 'open_ratio', value: 0.25, type: 'vos.Decimal', takesReadingsOnly: false },
    ]);
  });

  it('keeps the declared type even when the value reads like another one', () => {
    const [property] = withDeclaredTypes([['door_number', '4711']], resolved({ door_number: 'vos.String' }), writable);
    expect(property.type).toBe('vos.String');
  });

  it('drops a property the resolved set does not name a type for', () => {
    const paired = withDeclaredTypes(
      [['known', 1], ['unknown', 2]],
      resolved({ known: 'vos.LongInteger' }),
      writable,
    );
    expect(paired.map((p) => p.name)).toEqual(['known']);
  });

  it('offers nothing until the resolved set has arrived', () => {
    expect(withDeclaredTypes([['door_number', '4711']], null, writable)).toEqual([]);
  });

  it('marks each property the platform takes only from readings', () => {
    const paired = withDeclaredTypes(
      [['statedAreaHectares', 24], ['rainfallMillimetresPerYear', 700]],
      resolved({ statedAreaHectares: 'vos.Double', rainfallMillimetresPerYear: 'vos.Double' }),
      (name) => name === 'rainfallMillimetresPerYear',
    );
    expect(paired.map((p) => [p.name, p.takesReadingsOnly])).toEqual([
      ['statedAreaHectares', false],
      ['rainfallMillimetresPerYear', true],
    ]);
  });
});

describe('thingTakesReadingsOnly', () => {
  const site: VosThing = { Id: 's', Name: 'Willow Bend', Properties: {} };
  const archetype: VosThing = {
    Id: 'a', Name: 'Site',
    Properties: { rainfallMillimetresPerYear: null, statedAreaHectares: null },
    PropertyWriteKinds: { rainfallMillimetresPerYear: 'ObservationOnly', statedAreaHectares: 'FactOnly' },
  };
  const lookup: IsChainLookup = { byId: new Map([['a', archetype]]), isParents: new Map([['s', ['a']]]) };

  it('answers yes for a property whose archetype takes it only from readings', () => {
    expect(thingTakesReadingsOnly(site, lookup)('rainfallMillimetresPerYear')).toBe(true);
  });

  it('answers no for a property of another write kind', () => {
    expect(thingTakesReadingsOnly(site, lookup)('statedAreaHectares')).toBe(false);
  });
});

describe('relationshipTakesReadingsOnly', () => {
  const relationship: VosRelationship = {
    Id: 'r', SubjectId: 's', PredicateId: 'p', TargetId: 't',
    Properties: { flowLitresPerSecond: 1.5, weight: 0.5 },
    PropertyWriteKinds: { flowLitresPerSecond: 'ObservationOnly' },
  };

  it('answers yes for a property the relationship takes only from readings', () => {
    expect(relationshipTakesReadingsOnly(relationship)('flowLitresPerSecond')).toBe(true);
  });

  it('answers no for a property that declares nothing', () => {
    expect(relationshipTakesReadingsOnly(relationship)('weight')).toBe(false);
  });
});
