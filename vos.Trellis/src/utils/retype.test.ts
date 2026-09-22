import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../api/relationshipApi', () => ({
  relationshipApi: { create: vi.fn(), remove: vi.fn() },
}));

import { retypeThing } from './retype';
import { relationshipApi } from '../api/relationshipApi';
import type { VosRelationship } from '../types/vos';

const relationship = (Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship =>
  ({ Id, Name: '', SubjectId, PredicateId, TargetId, Properties: {} } as unknown as VosRelationship);

beforeEach(() => vi.clearAllMocks());

describe('retypeThing', () => {
  it('removes the thing’s is-edges and creates one to the new archetype', async () => {
    const relationships = [
      relationship('r1', 't', 'is', 'A'),        // the current type edge — remove
      relationship('r2', 't', 'contains', 'X'),  // not an is-edge — keep
      relationship('r3', 'other', 'is', 'A'),    // different subject — keep
    ];

    await retypeThing('t', 'B', 'is', relationships);

    expect(relationshipApi.remove).toHaveBeenCalledWith('r1');
    expect(relationshipApi.remove).toHaveBeenCalledTimes(1);
    expect(relationshipApi.create).toHaveBeenCalledWith('t', 'is', 'B');
  });

  it('creates the type edge even when the thing was previously untyped', async () => {
    await retypeThing('t', 'B', 'is', [relationship('r2', 't', 'contains', 'X')]);
    expect(relationshipApi.remove).not.toHaveBeenCalled();
    expect(relationshipApi.create).toHaveBeenCalledWith('t', 'is', 'B');
  });

  it('with a from-archetype, replaces only that type edge and preserves the others (multiple inheritance)', async () => {
    const relationships = [relationship('rA', 't', 'is', 'A'), relationship('rB', 't', 'is', 'B')];
    await retypeThing('t', 'C', 'is', relationships, 'A');
    expect(relationshipApi.remove).toHaveBeenCalledWith('rA');
    expect(relationshipApi.remove).toHaveBeenCalledTimes(1); // rB (type B) survives
    expect(relationshipApi.create).toHaveBeenCalledWith('t', 'is', 'C');
  });
});
