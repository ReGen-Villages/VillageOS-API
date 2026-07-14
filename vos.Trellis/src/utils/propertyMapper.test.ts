import { describe, it, expect } from 'vitest';
import { unwrapProperties, unwrapThing, unwrapRelationship, effectiveProperties } from './propertyMapper';
import type { VosThing, VosRelationship, InheritedPropertySet } from '../types/vos';

const inheritedSet = (
  source: string,
  props: Record<string, unknown>,
  inherited?: Record<string, InheritedPropertySet>,
): InheritedPropertySet => ({
  SourceId: source.toLowerCase(),
  SourceName: source,
  InheritedAt: '2024-01-01',
  Properties: props,
  ...(inherited ? { Inherited: inherited } : {}),
});

describe('unwrapProperties', () => {
  it('returns empty object for null', () => {
    expect(unwrapProperties(null)).toEqual({});
  });

  it('returns empty object for undefined', () => {
    expect(unwrapProperties(undefined)).toEqual({});
  });

  it('extracts value from typed envelope', () => {
    const props = { name: { typeInfo: 'vos.String', value: 'Alice' } };
    expect(unwrapProperties(props)).toEqual({ name: 'Alice' });
  });

  it('passes through plain values', () => {
    const props = { count: 42, label: 'test' };
    expect(unwrapProperties(props)).toEqual({ count: 42, label: 'test' });
  });

  it('handles mixed typed and plain values', () => {
    const props = {
      typed: { typeInfo: 'vos.Double', value: 3.14 },
      plain: 'hello',
    };
    expect(unwrapProperties(props)).toEqual({ typed: 3.14, plain: 'hello' });
  });

  it('handles null value inside envelope', () => {
    const props = { empty: { typeInfo: 'vos.String', value: null } };
    expect(unwrapProperties(props)).toEqual({ empty: null });
  });

  it('handles boolean envelope', () => {
    const props = { flag: { typeInfo: 'vos.Boolean', value: true } };
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
      Properties: { temp: { typeInfo: 'vos.Double', value: 36.6 } },
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
          Properties: { inherited: { typeInfo: 'vos.Integer', value: 99 } },
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

  // Regression (Bug #5932): the platform renamed the inherited-value field
  // InheritedProperties -> InheritedOverrides. unwrapThing must read either, or
  // inherited values silently vanish from the GUI.
  it('reads inherited values from the renamed InheritedOverrides field', () => {
    const thing = {
      ...baseThing,
      InheritedOverrides: {
        Parent: {
          SourceId: 'p1',
          SourceName: 'Parent',
          InheritedAt: '2024-01-01',
          Properties: { inherited: { typeInfo: 'vos.Integer', value: 99 } },
        },
      },
    } as unknown as VosThing;
    const result = unwrapThing(thing);
    expect(result.InheritedProperties?.Parent.Properties).toEqual({ inherited: 99 });
  });
});

// Regression (Bug #5932): under lazy inheritance an instance's value for an
// inherited name is relocated out of Properties into InheritedProperties, so
// reading Properties alone misses it and dashboard widgets render blank/0.
describe('effectiveProperties', () => {
  it('returns an inherited value when the own Properties are empty', () => {
    const merged = effectiveProperties({
      Properties: {},
      InheritedProperties: { Home: inheritedSet('Home', { energy_rating: 'A+' }) },
    });
    expect(merged).toEqual({ energy_rating: 'A+' });
  });

  it('lets an own value win over an inherited one of the same name', () => {
    const merged = effectiveProperties({
      Properties: { energy_rating: 'B' },
      InheritedProperties: { Home: inheritedSet('Home', { energy_rating: 'A+' }) },
    });
    expect(merged.energy_rating).toBe('B');
  });

  it('resolves a multi-level ancestor chain with nearer ancestors winning', () => {
    const merged = effectiveProperties({
      Properties: {},
      InheritedProperties: {
        // Home overrides x=2; its ancestor Building sets x=1 and y=9.
        Home: inheritedSet('Home', { x: 2 }, { Building: inheritedSet('Building', { x: 1, y: 9 }) }),
      },
    });
    expect(merged).toEqual({ x: 2, y: 9 });
  });

  it('returns own properties unchanged when there are no inherited overrides', () => {
    const merged = effectiveProperties({ Properties: { a: 1 }, InheritedProperties: undefined });
    expect(merged).toEqual({ a: 1 });
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
      Properties: { weight: { typeInfo: 'vos.Double', value: 0.5 } },
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
