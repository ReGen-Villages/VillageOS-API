import { describe, it, expect } from 'vitest';
import type { VosRelationship, VosThing } from '../types/vos';
import { buildModelIndex, thingIdBoundBy, thingsOfArchetypeBoundBy } from './dashboardApi';

const thing = (Id: string, Name: string, IsArchetype = false): VosThing => ({ Id, Name, IsArchetype, Properties: {} }) as unknown as VosThing;
const edge = (Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship =>
  ({ Id, SubjectId, PredicateId, TargetId, Properties: {} }) as VosRelationship;

const ROLE = 'spring-role';
const things = [
  thing('is', 'is'),
  thing('binds', 'binds'),
  thing(ROLE, 'SpringRole'),
  thing('spring-kind', 'Spring', true),
  thing('reservoir-kind', 'Reservoir', true),
  thing('spring-1', 'SPRING-1'),
  thing('reservoir-1', 'RESERVOIR-1'),
];
const instances = [
  edge('spring-1-is', 'spring-1', 'is', 'spring-kind'),
  edge('reservoir-1-is', 'reservoir-1', 'is', 'reservoir-kind'),
];
const boundOnce = [...instances, edge('role-binds-spring', ROLE, 'binds', 'spring-kind')];
const boundTwice = [...boundOnce, edge('role-binds-reservoir', ROLE, 'binds', 'reservoir-kind')];

describe('the Thing a role binds', () => {
  it('answers the one Thing the role binds, and the instances beneath it', () => {
    const index = buildModelIndex(things, boundOnce);

    expect(thingIdBoundBy(ROLE, index)).toBe('spring-kind');
    expect(thingsOfArchetypeBoundBy(ROLE, index).map((one) => one.Id)).toEqual(['spring-1']);
  });

  it('answers nothing where the role binds two Things, rather than whichever binding came first', () => {
    const index = buildModelIndex(things, boundTwice);

    expect(thingIdBoundBy(ROLE, index)).toBeUndefined();
    expect(thingsOfArchetypeBoundBy(ROLE, index)).toEqual([]);
  });
});
