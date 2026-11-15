import { describe, it, expect } from 'vitest';
import type { VosRelationship, VosThing } from '../types/vos';
import { buildModelIndex } from './dashboardApi';
import { PREDICATE_ROLE_ID } from './platformRoles';

const thing = (Id: string, Name: string, IsArchetype = false): VosThing => ({ Id, Name, IsArchetype, Properties: {} }) as unknown as VosThing;
const edge = (Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship =>
  ({ Id, SubjectId, PredicateId, TargetId, Properties: {} }) as VosRelationship;

// A model before any spring has been judged: `judgedAs` is declared a predicate, and nothing runs along it yet.
const things = [
  thing('is', 'is'),
  thing('binds', 'binds'),
  thing(PREDICATE_ROLE_ID, 'Predicate'),
  thing('predicate-root', 'Predicate', true),
  thing('judgedAs', 'judgedAs'),
  thing('spring', 'SPRING-1'),
];
const relationships = [
  edge('role-binds-root', PREDICATE_ROLE_ID, 'binds', 'predicate-root'),
  edge('judgedAs-is-predicate', 'judgedAs', 'is', 'predicate-root'),
];

describe('the predicates a model index knows', () => {
  it('knows a declared predicate by its is Predicate relationship before any relationship runs along it', () => {
    const index = buildModelIndex(things, relationships);

    expect(index.predicateNameToId.get('judgedAs')).toBe('judgedAs');
    expect(index.predicateIdToName.get('judgedAs')).toBe('judgedAs');
  });

  it('finds the predicate root through the role that binds it, not by its name', () => {
    const renamed = things.map((one) => (one.Id === 'predicate-root' ? thing('predicate-root', 'Verb', true) : one));

    expect(buildModelIndex(renamed, relationships).predicateNameToId.get('judgedAs')).toBe('judgedAs');
  });

  it('still knows a predicate a relationship uses where the model binds no predicate role', () => {
    const unbound = relationships.filter((one) => one.Id !== 'role-binds-root');

    const index = buildModelIndex(things, [...unbound, edge('spring-judged', 'spring', 'judgedAs', 'spring')]);

    expect(index.predicateNameToId.get('judgedAs')).toBe('judgedAs');
  });
});
