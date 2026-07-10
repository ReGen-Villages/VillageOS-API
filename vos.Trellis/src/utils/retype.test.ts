import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../api/relationshipApi', () => ({
  relationshipApi: { create: vi.fn(), remove: vi.fn() },
}));

import { retypeThing } from './retype';
import { relationshipApi } from '../api/relationshipApi';
import type { VosRelationship } from '../types/vos';

const rel = (Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship =>
  ({ Id, Name: '', SubjectId, PredicateId, TargetId, Properties: {} } as unknown as VosRelationship);

beforeEach(() => vi.clearAllMocks());

describe('retypeThing', () => {
  it('removes the thing’s is-edges and creates one to the new archetype', async () => {
    const rels = [
      rel('r1', 't', 'is', 'A'),        // the current type edge — remove
      rel('r2', 't', 'contains', 'X'),  // not an is-edge — keep
      rel('r3', 'other', 'is', 'A'),    // different subject — keep
    ];

    await retypeThing('t', 'B', 'is', rels);

    expect(relationshipApi.remove).toHaveBeenCalledWith('r1');
    expect(relationshipApi.remove).toHaveBeenCalledTimes(1);
    expect(relationshipApi.create).toHaveBeenCalledWith('t', 'is', 'B');
  });

  it('creates the type edge even when the thing was previously untyped', async () => {
    await retypeThing('t', 'B', 'is', [rel('r2', 't', 'contains', 'X')]);
    expect(relationshipApi.remove).not.toHaveBeenCalled();
    expect(relationshipApi.create).toHaveBeenCalledWith('t', 'is', 'B');
  });
});
