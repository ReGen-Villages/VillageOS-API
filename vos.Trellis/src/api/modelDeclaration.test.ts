import { describe, it, expect } from 'vitest';
import type { VosThing, VosRelationship } from '../types/vos';
import { buildModelIndex } from './dashboardApi';
import { edgesFrom, kindsOffered, propertiesOf, statesOf } from './modelDeclaration';

function thing(Id: string, Name: string, Properties: Record<string, unknown> = {}, IsArchetype = false): VosThing {
  return { Id, Name, Properties, IsArchetype };
}
function relationship(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

/**
 * A site model three kinds deep: a Spring is a Source is a Feature. `capacity` is declared on
 * Feature, `flow` on Spring, and `__hidden` is the platform's own. Two springs feed one reservoir;
 * one sits in a catchment.
 */
function site() {
  return buildModelIndex(
    [
      thing('is', 'is'),
      thing('feeds', 'feeds'),
      thing('within', 'within'),
      thing('feature', 'Feature', { capacity: 0 }, true),
      thing('source', 'Source', {}, true),
      thing('spring', 'Spring', { flow: 0, __hidden: true, geometry: 'POINT' }, true),
      thing('reservoir', 'Reservoir', {}, true),
      thing('catchment', 'Catchment', {}, true),
      thing('emptyKind', 'Wellhead', {}, true),
      thing('s1', 'SPRING-1', { flow: 12.5, note: 'seasonal' }),
      thing('s2', 'SPRING-2', { flow: 3 }),
      thing('r1', 'RESERVOIR-1'),
      thing('c1', 'CATCHMENT-1'),
    ],
    [
      relationship('i1', 'spring', 'is', 'source'),
      relationship('i2', 'source', 'is', 'feature'),
      relationship('i3', 's1', 'is', 'spring'),
      relationship('i4', 's2', 'is', 'spring'),
      relationship('i5', 'r1', 'is', 'reservoir'),
      relationship('i6', 'c1', 'is', 'catchment'),
      relationship('e1', 's1', 'feeds', 'r1'),
      relationship('e2', 's2', 'feeds', 'r1'),
      relationship('e3', 'c1', 'within', 's1'),
    ],
  );
}

describe('the properties a kind declares', () => {
  it('offers a property declared two kinds up with the kind that declares it', () => {
    const offered = propertiesOf('Spring', site());
    expect(offered.find((p) => p.name === 'capacity')).toMatchObject({ declaredBy: 'Feature' });
    expect(offered.find((p) => p.name === 'flow')).toMatchObject({ declaredBy: 'Spring', numeric: true });
  });

  it('reads an example off an instance rather than the declaration', () => {
    expect(propertiesOf('Spring', site()).find((p) => p.name === 'flow')?.example).toBe(12.5);
  });

  it('leaves the platform’s own marks and the geometry out', () => {
    const names = propertiesOf('Spring', site()).map((p) => p.name);
    expect(names).not.toContain('__hidden');
    expect(names).not.toContain('geometry');
  });

  it('offers nothing for a word that is not a kind', () => {
    expect(propertiesOf('SPRING-1', site())).toEqual([]);
    expect(propertiesOf('Nowhere', site())).toEqual([]);
  });
});

describe('the links a kind’s instances carry', () => {
  it('names the far-end kind and counts the instances carrying each', () => {
    const links = edgesFrom('Spring', site());
    expect(links).toContainEqual({ predicate: 'feeds', direction: 'out', reaches: 'Reservoir', count: 2 });
    expect(links).toContainEqual({ predicate: 'within', direction: 'in', reaches: 'Catchment', count: 1 });
  });

  it('never offers `is`, which types rather than relates', () => {
    expect(edgesFrom('Spring', site()).some((link) => link.predicate === 'is')).toBe(false);
  });

  it('offers a link only where an instance carries it', () => {
    expect(edgesFrom('Catchment', site())).toEqual([{ predicate: 'within', direction: 'out', reaches: 'Spring', count: 1 }]);
    expect(edgesFrom('Wellhead', site())).toEqual([]);
  });
});

describe('the kinds offered', () => {
  it('offers every kind with an instance below it, in name order, and no kind without one', () => {
    expect(kindsOffered(site())).toEqual(['Catchment', 'Feature', 'Reservoir', 'Source', 'Spring']);
  });
});

describe('the states a kind derives', () => {
  it('lists own and inherited states once each, in name order', () => {
    const states = statesOf({
      OwnRanges: [{ Name: 'dry' }, { Name: 'flowing' }] as never,
      InheritedRanges: [
        { SourceId: 'source', SourceName: 'Source', InheritedAt: '', Ranges: [{ Name: 'flowing' }] as never, Inherited: [
          { SourceId: 'feature', SourceName: 'Feature', InheritedAt: '', Ranges: [{ Name: 'abandoned' }] as never, Inherited: [] },
        ] },
      ],
    });
    expect(states).toEqual(['abandoned', 'dry', 'flowing']);
  });
});
