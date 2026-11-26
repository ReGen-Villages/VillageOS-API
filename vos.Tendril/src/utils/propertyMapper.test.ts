import { describe, it, expect } from 'vitest';
import { unwrapProperties, unwrapThing, unwrapRelationship, effectiveProperties, statedProperties, type IsChainLookup } from './propertyMapper';
import type { VosThing, VosRelationship, InheritedPropertySet } from '../types/vos';

const inheritedSet = (
  source: string,
  props: Record<string, unknown>,
  inherited?: Record<string, InheritedPropertySet>,
): InheritedPropertySet => ({
  SourceId: source.toLowerCase(),
  SourceName: source,
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
      InheritedOverrides: {
        ParentType: {
          SourceId: 'p1',
          SourceName: 'Parent',
          Properties: { inherited: { typeInfo: 'vos.Integer', value: 99 } },
        },
      },
    };
    const result = unwrapThing(thing);
    expect(result.InheritedOverrides?.ParentType.Properties).toEqual({ inherited: 99 });
  });

  it('handles null Properties', () => {
    const thing = { ...baseThing, Properties: null } as unknown as VosThing;
    const result = unwrapThing(thing);
    expect(result.Properties).toEqual({});
  });

  it('handles missing InheritedOverrides', () => {
    const result = unwrapThing(baseThing);
    expect(result.InheritedOverrides).toBeUndefined();
  });

  // The wire field is InheritedOverrides (override-only); unwrapThing has to read it, or inherited
  // override values silently vanish from the page.
  it('reads inherited override values from the InheritedOverrides field', () => {
    const thing = {
      ...baseThing,
      InheritedOverrides: {
        Parent: {
          SourceId: 'p1',
          SourceName: 'Parent',
          Properties: { inherited: { typeInfo: 'vos.Integer', value: 99 } },
        },
      },
    } as unknown as VosThing;
    const result = unwrapThing(thing);
    expect(result.InheritedOverrides?.Parent.Properties).toEqual({ inherited: 99 });
  });
});

// An instance's value for a name it inherits is held in InheritedOverrides rather than Properties, so
// reading Properties alone misses it and dashboard widgets render blank.
describe('effectiveProperties', () => {
  const noAncestors: IsChainLookup = { byId: new Map(), isParents: new Map() };

  const chain = (things: VosThing[], parents: Record<string, string[]>): IsChainLookup => ({
    byId: new Map(things.map((t) => [t.Id, t])),
    isParents: new Map(Object.entries(parents)),
  });
  const archetype = (id: string, name: string, props: Record<string, unknown>): VosThing =>
    ({ Id: id, Name: name, Properties: props });

  it('returns an override value when the own Properties are empty', () => {
    const merged = effectiveProperties({
      Properties: {},
      InheritedOverrides: { Home: inheritedSet('Home', { energy_rating: 'A+' }) },
    }, noAncestors);
    expect(merged).toEqual({ energy_rating: 'A+' });
  });

  it('lets an own value win over an inherited one of the same name', () => {
    const merged = effectiveProperties({
      Properties: { energy_rating: 'B' },
      InheritedOverrides: { Home: inheritedSet('Home', { energy_rating: 'A+' }) },
    }, noAncestors);
    expect(merged.energy_rating).toBe('B');
  });

  it('resolves a multi-level override chain with nearer sources winning', () => {
    const merged = effectiveProperties({
      Properties: {},
      InheritedOverrides: {
        Home: inheritedSet('Home', { x: 2 }, { Building: inheritedSet('Building', { x: 1, y: 9 }) }),
      },
    }, noAncestors);
    expect(merged).toEqual({ x: 2, y: 9 });
  });

  it('returns own properties unchanged when there are no inherited overrides', () => {
    const merged = effectiveProperties({ Properties: { a: 1 }, InheritedOverrides: undefined }, noAncestors);
    expect(merged).toEqual({ a: 1 });
  });

  // A default lives on the archetype, not in the instance, so it is reached only up the is-chain.
  it('resolves a non-overridden inherited default from the archetype', () => {
    const home = archetype('h', 'Home', { energy_rating: 'A+' });
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: undefined };
    const merged = effectiveProperties(instance, chain([home], { i: ['h'] }));
    expect(merged).toEqual({ energy_rating: 'A+' });
  });

  it('lets an instance override win over the archetype default', () => {
    const home = archetype('home', 'Home', { energy_rating: 'A+' });
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: { Home: inheritedSet('Home', { energy_rating: 'B' }) } };
    expect(effectiveProperties(instance, chain([home], { i: ['home'] })).energy_rating).toBe('B');
  });

  it('lets an own value win over the archetype default', () => {
    const home = archetype('h', 'Home', { x: 'default' });
    const instance = { Id: 'i', Properties: { x: 'own' }, InheritedOverrides: undefined };
    expect(effectiveProperties(instance, chain([home], { i: ['h'] })).x).toBe('own');
  });

  it('lets a nearer archetype default win over a farther one', () => {
    const building = archetype('b', 'Building', { x: 'building', y: 'shared' });
    const home = archetype('h', 'Home', { x: 'home' });
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: undefined };
    const merged = effectiveProperties(instance, chain([home, building], { i: ['h'], h: ['b'] }));
    expect(merged).toEqual({ x: 'home', y: 'shared' });
  });

  // The platform reads a name two sources declare as ambiguous and computes nothing from it, so a
  // page that picked one would state a value the platform will not use.
  it('gives no value for a name two unrelated archetypes each declare, and keeps the names only one declares', () => {
    const zone = archetype('z', 'Zone', { x: 'zone', y: 'only zone' });
    const field = archetype('f', 'Åker', { x: 'åker' });
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: undefined };
    const merged = effectiveProperties(instance, chain([zone, field], { i: ['z', 'f'] }));
    expect('x' in merged).toBe(false);
    expect(merged.y).toBe('only zone');
  });

  it('gives no value for the ambiguous name whatever order the archetypes arrive in', () => {
    const zone = archetype('z', 'Zone', { x: 'zone' });
    const array = archetype('a', 'array', { x: 'array' });
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: undefined };
    expect('x' in effectiveProperties(instance, chain([zone, array], { i: ['z', 'a'] }))).toBe(false);
    expect('x' in effectiveProperties(instance, chain([zone, array], { i: ['a', 'z'] }))).toBe(false);
  });

  it('counts a name two archetypes declare as ambiguous even where one of them holds no value for it', () => {
    const zone = archetype('z', 'Zone', { x: null });
    const field = archetype('f', 'Åker', { x: 'åker' });
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: undefined };
    expect('x' in effectiveProperties(instance, chain([zone, field], { i: ['z', 'f'] }))).toBe(false);
  });

  it('lets a Thing\'s own value win over two archetypes that each declare the name', () => {
    const zone = archetype('z', 'Zone', { x: 'zone' });
    const field = archetype('f', 'Åker', { x: 'åker' });
    const instance = { Id: 'i', Properties: { x: 'own' }, InheritedOverrides: undefined };
    expect(effectiveProperties(instance, chain([zone, field], { i: ['z', 'f'] })).x).toBe('own');
  });

  // The platform holds a value written for an inherited name against the source that declared it, so
  // the Thing's value does not settle which of two sources the name means.
  it('gives no value for a name the Thing stored against one of two archetypes that each declare it', () => {
    const zone = archetype('z', 'Zone', { x: 'zone' });
    const field = archetype('f', 'Åker', { x: 'åker' });
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: { z: { ...inheritedSet('Zone', { x: 'stored' }), SourceId: 'z' } } };
    expect('x' in effectiveProperties(instance, chain([zone, field], { i: ['z', 'f'] }))).toBe(false);
  });

  it('gives no value for a name the Thing stored against two sources', () => {
    const instance = {
      Id: 'i',
      Properties: {},
      InheritedOverrides: { z: inheritedSet('Zone', { x: 'zone' }), a: inheritedSet('array', { x: 'array' }) },
    };
    expect('x' in effectiveProperties(instance, noAncestors)).toBe(false);
  });

  it('gives a member of an archetype that is two such kinds no value for the name either', () => {
    const zone = archetype('z', 'Zone', { x: 'zone' });
    const field = archetype('f', 'Åker', { x: 'åker' });
    const plot = archetype('p', 'Plot', {});
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: undefined };
    expect('x' in effectiveProperties(instance, chain([zone, field, plot], { i: ['p'], p: ['z', 'f'] }))).toBe(false);
  });

  // The platform holds a kind's value for a name the kind above it declares against that kind, so
  // there is one source and the nearer value is read.
  it('reads a kind that states its own value over the kind above it as one source', () => {
    const dwelling = archetype('d', 'Dwelling', { factor: 1.25 });
    const cottage = {
      ...archetype('c', 'Cottage', {}),
      InheritedOverrides: { d: { ...inheritedSet('Dwelling', { factor: 2.75 }), SourceId: 'd' } },
    };
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: undefined };
    expect(effectiveProperties(instance, chain([dwelling, cottage], { i: ['c'], c: ['d'] })).factor).toBe(2.75);
  });

  it('reads a name two archetypes both inherit from one kind as one source', () => {
    const base = archetype('b', 'Building', { x: 'building' });
    const home = archetype('h', 'Home', {});
    const office = archetype('o', 'Office', {});
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: undefined };
    expect(effectiveProperties(instance, chain([base, home, office], { i: ['h', 'o'], h: ['b'], o: ['b'] })).x).toBe('building');
  });

  it('survives a malformed is-cycle without infinite recursion', () => {
    const a = archetype('a', 'A', { x: 1 });
    const b = archetype('b', 'B', { y: 2 });
    const merged = effectiveProperties(a, chain([a, b], { a: ['b'], b: ['a'] }));
    expect(merged).toEqual({ x: 1, y: 2 });
  });

  // Within a stable model (same index) a repeat call returns the very same frozen object.
  it('memoizes per model index and freezes the result', () => {
    const thing = { Id: 'i', Properties: { a: 1 }, InheritedOverrides: { Home: inheritedSet('Home', { b: 2 }) } };
    const first = effectiveProperties(thing, noAncestors);
    const second = effectiveProperties(thing, noAncestors);
    expect(second).toBe(first); // same reference — computed once
    expect(Object.isFrozen(first)).toBe(true);
    expect(() => { (first as Record<string, unknown>).a = 99; }).toThrow();
  });

  // The cache is keyed on the model index, so when an archetype default changes the
  // instance's effective value updates — a Thing-only cache would have gone stale.
  it('invalidates when the model index changes (archetype default updated)', () => {
    const instance = { Id: 'i', Properties: {}, InheritedOverrides: undefined };
    const before = effectiveProperties(instance, chain([archetype('h', 'Home', { rate: 'A+' })], { i: ['h'] }));
    expect(before.rate).toBe('A+');
    // A new index (as buildModelIndex produces on any model change) carries the updated archetype.
    const after = effectiveProperties(instance, chain([archetype('h', 'Home', { rate: 'A-' })], { i: ['h'] }));
    expect(after.rate).toBe('A-');
  });
});

describe('unwrapRelationship', () => {
  const baseRelationship: VosRelationship = {
    Id: 'r1',
    Name: 'TestRel',
    SubjectId: 's1',
    TargetId: 't1',
    PredicateId: 'p1',
    Properties: {},
  };

  it('unwraps relationship properties', () => {
    const relationship: VosRelationship = {
      ...baseRelationship,
      Properties: { weight: { typeInfo: 'vos.Double', value: 0.5 } },
    };
    const result = unwrapRelationship(relationship);
    expect(result.Properties).toEqual({ weight: 0.5 });
  });

  it('preserves non-property fields', () => {
    const result = unwrapRelationship(baseRelationship);
    expect(result.Id).toBe('r1');
    expect(result.SubjectId).toBe('s1');
    expect(result.TargetId).toBe('t1');
    expect(result.PredicateId).toBe('p1');
  });

  it('keeps the write kind each property arrives with', () => {
    const result = unwrapRelationship({
      ...baseRelationship,
      Properties: {
        flowLitresPerSecond: { typeInfo: 'vos.Double', value: 1.5, writeKind: 'ObservationOnly' },
        weight: { typeInfo: 'vos.Double', value: 0.5 },
      },
    });
    expect(result.PropertyWriteKinds).toEqual({ flowLitresPerSecond: 'ObservationOnly' });
  });

  it('records nothing for a relationship whose properties declare nothing', () => {
    const result = unwrapRelationship({
      ...baseRelationship,
      Properties: { weight: { typeInfo: 'vos.Double', value: 0.5 } },
    });
    expect(result.PropertyWriteKinds).toBeUndefined();
  });
});

// Unwrapping keeps only what a value is, and the declaration is the model's only
// record of how it came to be one — dropped here, no page can ever say where a figure came from.
describe('the declaration a value arrives with', () => {
  it('keeps the write kind of an own property', () => {
    const thing = unwrapThing({
      Id: 't', Name: 'Willow Bend',
      Properties: {
        latitude: { typeInfo: 'vos.Double', value: 39.5, writeKind: 'FactOnly' },
        elevationMetres: { typeInfo: 'vos.Double', value: 210, writeKind: 'ObservationOnly' },
      },
    });
    expect(thing.PropertyWriteKinds).toEqual({ latitude: 'FactOnly', elevationMetres: 'ObservationOnly' });
    expect(thing.Properties).toEqual({ latitude: 39.5, elevationMetres: 210 });
  });

  // Where the platform puts a value written onto a name an archetype declares.
  it('keeps the write kind of a value stored under an inherited name', () => {
    const thing = unwrapThing({
      Id: 't', Name: 'Willow Bend',
      Properties: {},
      InheritedOverrides: {
        Site: {
          SourceId: 'site', SourceName: 'Site',
          Properties: { statedAreaHectares: { typeInfo: 'vos.Double', value: 24, writeKind: 'FactOnly' } },
        },
      },
    });
    expect(thing.InheritedOverrides?.Site.PropertyWriteKinds).toEqual({ statedAreaHectares: 'FactOnly' });
  });

  // A property open to either kind of write carries no declaration, and neither should the client:
  // saying nothing is what keeps silence apart from a declaration that a value was sampled.
  it('records nothing for a property the model declares nothing about', () => {
    const thing = unwrapThing({
      Id: 't', Name: 'Willow Bend',
      Properties: { anything: { typeInfo: 'vos.Double', value: 1 } },
    });
    expect(thing.PropertyWriteKinds).toBeUndefined();
  });

  it('records nothing for a value that arrives with no envelope around it', () => {
    const thing = unwrapThing({
      Id: 't', Name: 'Willow Bend',
      Properties: { alreadyPlain: 39.5, nothingAtAll: null },
    });
    expect(thing.PropertyWriteKinds).toBeUndefined();
  });

  it('ignores a write kind the platform has no such name for', () => {
    const thing = unwrapThing({
      Id: 't', Name: 'Willow Bend',
      Properties: { anything: { typeInfo: 'vos.Double', value: 1, writeKind: 'Whenever' } },
    });
    expect(thing.PropertyWriteKinds).toBeUndefined();
  });
});

// What a Thing states for itself: its own properties and its override store, never the `is` chain.
describe('statedProperties', () => {
  const thing = (properties: Record<string, unknown>, overrides?: Record<string, InheritedPropertySet>): VosThing =>
    ({ Id: 't', Name: 'port', Properties: properties, ...(overrides ? { InheritedOverrides: overrides } : {}) });
  const set = (values: Record<string, unknown>, inherited?: Record<string, InheritedPropertySet>): InheritedPropertySet =>
    ({ SourceId: 's', SourceName: 'Socket', Properties: values, ...(inherited ? { Inherited: inherited } : {}) });

  it('reads a value from the override store', () => {
    expect(statedProperties(thing({}, { a: set({ portName: 'subject' }) }))).toEqual({ portName: 'subject' });
  });

  it('lets an own value win over an override of the same name', () => {
    expect(statedProperties(thing({ portName: 'own' }, { a: set({ portName: 'overridden' }) })).portName).toBe('own');
  });

  it('reads a value stated over a farther ancestor', () => {
    expect(statedProperties(thing({}, { a: set({}, { b: set({ direction: 'out' }) }) })).direction).toBe('out');
  });

  it('states nothing for a Thing with no override store', () => {
    expect(statedProperties(thing({ x: 1 }))).toEqual({ x: 1 });
  });
});
