import { describe, it, expect } from 'vitest';
import { valueOrigin } from './propertyOrigin';
import type { IsChainLookup } from './propertyMapper';
import type { DeclaredWriteKind, VosThing } from '../types/vos';

const thing = (
  id: string,
  name: string,
  parts: Partial<VosThing> = {},
): VosThing => ({ Id: id, Name: name, Properties: {}, ...parts });

const chain = (things: VosThing[], parents: Record<string, string[]>): IsChainLookup => ({
  byId: new Map(things.map((t) => [t.Id, t])),
  isParents: new Map(Object.entries(parents)),
});

const alone: IsChainLookup = { byId: new Map(), isParents: new Map() };

/** A value stored under a name the archetype declares, which is where the platform puts one. */
const stored = (
  source: string,
  values: Record<string, unknown>,
  kinds?: Record<string, DeclaredWriteKind>,
) => ({
  [source]: {
    SourceId: source,
    SourceName: source,
    InheritedAt: '2026-08-01T00:00:00Z',
    Properties: values,
    PropertyWriteKinds: kinds,
  },
});

describe('valueOrigin', () => {
  it('reads a value the submitter asserted as stated', () => {
    const site = thing('s', 'Willow Bend', {
      Properties: { statedAreaHectares: 24 },
      PropertyWriteKinds: { statedAreaHectares: 'FactOnly' },
    });
    expect(valueOrigin(site, 'statedAreaHectares', alone))
      .toEqual({ origin: 'stated', assumedFrom: null });
  });

  it('reads a value sampled or fetched from outside as measured', () => {
    const site = thing('s', 'Willow Bend', {
      Properties: { rainfallMillimetresPerYear: 700 },
      PropertyWriteKinds: { rainfallMillimetresPerYear: 'ObservationOnly' },
    });
    expect(valueOrigin(site, 'rainfallMillimetresPerYear', alone))
      .toEqual({ origin: 'measured', assumedFrom: null });
  });

  // The two the design exists to separate: one property name each, and nothing about either name
  // says which is which.
  it('separates a stated figure from a fetched one on the same Thing', () => {
    const site = thing('s', 'Willow Bend', {
      Properties: { latitude: 39.5, elevationMetres: 210 },
      PropertyWriteKinds: { latitude: 'FactOnly', elevationMetres: 'ObservationOnly' },
    });
    expect(valueOrigin(site, 'latitude', alone).origin).toBe('stated');
    expect(valueOrigin(site, 'elevationMetres', alone).origin).toBe('measured');
  });

  // The platform relocates a value written under an inherited name into the override store, so
  // reading own Properties alone would call every submitted figure an assumption.
  it('reads a value stored under an inherited name as the Thing\'s own claim', () => {
    const archetype = thing('a', 'Site', {
      Properties: { statedAreaHectares: null },
      PropertyWriteKinds: { statedAreaHectares: 'FactOnly' },
    });
    const site = thing('s', 'Willow Bend', {
      InheritedOverrides: stored('Site', { statedAreaHectares: 24 }, { statedAreaHectares: 'FactOnly' }),
    });
    expect(valueOrigin(site, 'statedAreaHectares', chain([archetype], { s: ['a'] })))
      .toEqual({ origin: 'stated', assumedFrom: null });
  });

  it('reads a value only the archetype carries as the platform\'s assumption, and names it', () => {
    const archetype = thing('a', 'Site', {
      Properties: { householdSize: 2.4 },
      PropertyWriteKinds: { householdSize: 'FactOnly' },
    });
    const site = thing('s', 'Willow Bend');
    expect(valueOrigin(site, 'householdSize', chain([archetype], { s: ['a'] })))
      .toEqual({ origin: 'assumed', assumedFrom: 'Site' });
  });

  it('names the Thing that stated an assumption, not the archetype it travelled through', () => {
    const far = thing('f', 'Place', { Properties: { householdSize: 2.4 } });
    const near = thing('n', 'Site');
    const site = thing('s', 'Willow Bend');
    expect(valueOrigin(site, 'householdSize', chain([far, near], { s: ['n'], n: ['f'] })).assumedFrom)
      .toBe('Place');
  });

  it('lets an assumption the Thing overrode become the Thing\'s own claim', () => {
    const archetype = thing('a', 'Site', {
      Properties: { householdSize: 2.4 },
      PropertyWriteKinds: { householdSize: 'FactOnly' },
    });
    const site = thing('s', 'Willow Bend', {
      InheritedOverrides: stored('Site', { householdSize: 3.1 }, { householdSize: 'FactOnly' }),
    });
    expect(valueOrigin(site, 'householdSize', chain([archetype], { s: ['a'] })))
      .toEqual({ origin: 'stated', assumedFrom: null });
  });

  // The failure the whole design exists to prevent: silence read as evidence.
  it('reads a value the model declares nothing about as unknown, never as measured', () => {
    const site = thing('s', 'Willow Bend', { Properties: { somethingUndeclared: 12 } });
    expect(valueOrigin(site, 'somethingUndeclared', alone))
      .toEqual({ origin: 'unknown', assumedFrom: null });
  });

  it('reads a declared property nothing has valued as unknown', () => {
    const archetype = thing('a', 'Site', {
      Properties: { solarResourceKwhPerM2PerYear: null },
      PropertyWriteKinds: { solarResourceKwhPerM2PerYear: 'ObservationOnly' },
    });
    const site = thing('s', 'Willow Bend');
    expect(valueOrigin(site, 'solarResourceKwhPerM2PerYear', chain([archetype], { s: ['a'] })))
      .toEqual({ origin: 'unknown', assumedFrom: null });
  });

  it('reads a property the Thing does not hold at all as unknown', () => {
    expect(valueOrigin(thing('s', 'Willow Bend'), 'absent', alone))
      .toEqual({ origin: 'unknown', assumedFrom: null });
  });

  // TC #6480: the origin comes from what the model declares, so the same value under any other
  // name answers the same way.
  it('decides the origin from the declaration, not from the property name', () => {
    const underOneName = thing('s', 'Willow Bend', {
      Properties: { rainfallMillimetresPerYear: 700 },
      PropertyWriteKinds: { rainfallMillimetresPerYear: 'ObservationOnly' },
    });
    const underAnother = thing('s', 'Willow Bend', {
      Properties: { howMuchItRains: 700 },
      PropertyWriteKinds: { howMuchItRains: 'ObservationOnly' },
    });
    expect(valueOrigin(underOneName, 'rainfallMillimetresPerYear', alone).origin)
      .toBe(valueOrigin(underAnother, 'howMuchItRains', alone).origin);
  });

  // A name that reads like a measurement is still only a name.
  it('does not read a measurement into a name that sounds like one', () => {
    const site = thing('s', 'Willow Bend', {
      Properties: { measuredAreaHectares: 23.4 },
      PropertyWriteKinds: { measuredAreaHectares: 'FactOnly' },
    });
    expect(valueOrigin(site, 'measuredAreaHectares', alone).origin).toBe('stated');
  });

  it('finds the declaration up the is-chain when the stored value carries none', () => {
    const archetype = thing('a', 'Site', {
      Properties: { elevationMetres: null },
      PropertyWriteKinds: { elevationMetres: 'ObservationOnly' },
    });
    const site = thing('s', 'Willow Bend', { Properties: { elevationMetres: 210 } });
    expect(valueOrigin(site, 'elevationMetres', chain([archetype], { s: ['a'] })).origin)
      .toBe('measured');
  });

  // Same rule effectiveProperties resolves the value by, so the origin names the Thing whose
  // value is the one on screen.
  it('names the sibling archetype whose value effectiveProperties lets win', () => {
    const alpha = thing('x', 'Alpha', { Properties: { householdSize: 2 } });
    const zulu = thing('z', 'Zulu', { Properties: { householdSize: 3 } });
    const site = thing('s', 'Willow Bend');
    expect(valueOrigin(site, 'householdSize', chain([alpha, zulu], { s: ['x', 'z'] })).assumedFrom)
      .toBe('Zulu');
  });

  // A value inherited through two archetypes is stored under the nearer one, with the farther one's
  // set nested inside it.
  it('reads a value stored under a nested inherited name as the Thing\'s own claim', () => {
    const site = thing('s', 'Willow Bend', {
      InheritedOverrides: {
        Site: {
          SourceId: 'Site', SourceName: 'Site', InheritedAt: '2026-08-01T00:00:00Z',
          Properties: {},
          Inherited: stored('Place', { latitude: 39.5 }, { latitude: 'FactOnly' }),
        },
      },
    });
    expect(valueOrigin(site, 'latitude', alone)).toEqual({ origin: 'stated', assumedFrom: null });
  });

  it('answers rather than hanging when the is-chain loops back on itself', () => {
    const first = thing('a', 'First');
    const second = thing('b', 'Second');
    expect(valueOrigin(first, 'anything', chain([first, second], { a: ['b'], b: ['a'] })))
      .toEqual({ origin: 'unknown', assumedFrom: null });
  });

  // The declaration is looked up along the same chain, so it needs its own guard: this one holds a
  // value, which is what carries the walk past where the last test stopped.
  it('answers rather than hanging when a loop is walked looking for the declaration', () => {
    const first = thing('a', 'First', { Properties: { anything: 1 } });
    const second = thing('b', 'Second');
    expect(valueOrigin(first, 'anything', chain([first, second], { a: ['b'], b: ['a'] })))
      .toEqual({ origin: 'unknown', assumedFrom: null });
  });
});
