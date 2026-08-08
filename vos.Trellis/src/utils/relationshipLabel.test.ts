import { describe, it, expect } from 'vitest';
import { relationshipLabel } from './relationshipLabel';
import type { VosRelationship } from '../types/vos';

const names = new Map([
  ['subject-id', 'Kitchen'],
  ['predicate-id', 'contains'],
  ['target-id', 'Sink'],
]);
const nameOf = (id: string) => names.get(id);

function relationship(overrides: Partial<VosRelationship> = {}): VosRelationship {
  return {
    Id: 'rel-id',
    SubjectId: 'subject-id',
    PredicateId: 'predicate-id',
    TargetId: 'target-id',
    Properties: {},
    ...overrides,
  };
}

describe('relationshipLabel', () => {
  it('composes subject, predicate and target when the platform sent no name', () => {
    expect(relationshipLabel(relationship(), nameOf)).toBe('Kitchen contains Sink');
  });

  it('uses the name the platform sent, because it is one the client could not work out', () => {
    expect(relationshipLabel(relationship({ Name: 'The one under the window' }), nameOf))
      .toBe('The one under the window');
  });

  it('falls back to a shortened id for an endpoint missing from the loaded model', () => {
    const label = relationshipLabel(
      relationship({ TargetId: '9f8e7d6c-1234-5678-9abc-def012345678' }),
      nameOf,
    );
    expect(label).toBe('Kitchen contains 9f8e7d6c...');
  });

  it('treats an empty name the same as an absent one rather than showing a blank label', () => {
    expect(relationshipLabel(relationship({ Name: '' }), nameOf)).toBe('Kitchen contains Sink');
  });
});
