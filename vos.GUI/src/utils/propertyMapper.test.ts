import { describe, it, expect } from 'vitest';
import { unwrapProperties, unwrapThing, unwrapRelationship } from './propertyMapper';
import type { VosThing, VosRelationship } from '../types/vos';

describe('unwrapProperties', () => {
  it('returns empty object for null', () => {
    expect(unwrapProperties(null)).toEqual({});
  });

  it('returns empty object for undefined', () => {
    expect(unwrapProperties(undefined)).toEqual({});
  });

  it('extracts value from typed envelope', () => {
    const props = { name: { typeInfo: 'System.String', value: 'Alice' } };
    expect(unwrapProperties(props)).toEqual({ name: 'Alice' });
  });

  it('passes through plain values', () => {
    const props = { count: 42, label: 'test' };
    expect(unwrapProperties(props)).toEqual({ count: 42, label: 'test' });
  });

  it('handles mixed typed and plain values', () => {
    const props = {
      typed: { typeInfo: 'System.Double', value: 3.14 },
      plain: 'hello',
    };
    expect(unwrapProperties(props)).toEqual({ typed: 3.14, plain: 'hello' });
  });

  it('handles null value inside envelope', () => {
    const props = { empty: { typeInfo: 'System.String', value: null } };
    expect(unwrapProperties(props)).toEqual({ empty: null });
  });

  it('handles boolean envelope', () => {
    const props = { flag: { typeInfo: 'System.Boolean', value: true } };
    expect(unwrapProperties(props)).toEqual({ flag: true });
  });

  it('handles object without value key as pass-through', () => {
    const nested = { foo: 'bar', baz: 123 };
    const props = { data: nested };
    expect(unwrapProperties(props)).toEqual({ data: nested });
  });
});

describe('unwrapThing', () => {
  const baseThing: VosThing = {
    Id: 't1',
    Name: 'TestThing',
    Properties: {},
  };

  it('unwraps own properties', () => {
    const thing: VosThing = {
      ...baseThing,
      Properties: { temp: { typeInfo: 'System.Double', value: 36.6 } },
    };
    const result = unwrapThing(thing);
    expect(result.Properties).toEqual({ temp: 36.6 });
  });

  it('unwraps inherited properties recursively', () => {
    const thing: VosThing = {
      ...baseThing,
      InheritedProperties: {
        ParentType: {
          SourceId: 'p1',
          SourceName: 'Parent',
          InheritedAt: '2024-01-01',
          Properties: { inherited: { typeInfo: 'System.Int32', value: 99 } },
        },
      },
    };
    const result = unwrapThing(thing);
    expect(result.InheritedProperties?.ParentType.Properties).toEqual({ inherited: 99 });
  });

  it('handles null Properties', () => {
    const thing = { ...baseThing, Properties: null } as unknown as VosThing;
    const result = unwrapThing(thing);
    expect(result.Properties).toEqual({});
  });

  it('handles missing InheritedProperties', () => {
    const result = unwrapThing(baseThing);
    expect(result.InheritedProperties).toBeUndefined();
  });
});

describe('unwrapRelationship', () => {
  const baseRel: VosRelationship = {
    Id: 'r1',
    Name: 'TestRel',
    SubjectId: 's1',
    TargetId: 't1',
    PredicateId: 'p1',
    Properties: {},
  };

  it('unwraps relationship properties', () => {
    const rel: VosRelationship = {
      ...baseRel,
      Properties: { weight: { typeInfo: 'System.Double', value: 0.5 } },
    };
    const result = unwrapRelationship(rel);
    expect(result.Properties).toEqual({ weight: 0.5 });
  });

  it('preserves non-property fields', () => {
    const result = unwrapRelationship(baseRel);
    expect(result.Id).toBe('r1');
    expect(result.SubjectId).toBe('s1');
    expect(result.TargetId).toBe('t1');
    expect(result.PredicateId).toBe('p1');
  });
});
