import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';

const mockGetAtInstant = vi.fn();

vi.mock('../../../api/thingApi', () => ({
  thingApi: {
    getAtInstant: (id: string, instant: string, signal?: AbortSignal) => mockGetAtInstant(id, instant, signal),
  },
}));

import { buildModelIndex } from '../../../api/dashboardApi';
import type { VosRelationship, VosThing } from '../../../types/vos';
import { useDecisionExplanations } from './useDecisionExplanations';
import { instantReadKey } from './decisionExplanation';

function thing(Id: string, Name: string, Properties: Record<string, unknown> = {}, IsArchetype = false): VosThing {
  return { Id, Name, Properties, IsArchetype };
}
function relationship(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, Name: Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

const INSTANT = '2026-09-22T09:14:03Z';

function index() {
  return buildModelIndex(
    [
      thing('is', 'is'),
      thing('constraintArchetype', 'Constraint', { __IsConstraintArchetype: true }, true),
      thing('about', 'about', { __IsDecisionSubjectPredicate: true }),
      thing('chose', 'chose', { __IsChosenPredicate: true }),
      thing('support', 'support', { __IsSupportPredicate: true }),
      thing('because', 'because', { __IsConsiderationCausePredicate: true }),
      thing('decision', 'sourcing-1', { decidedAt: INSTANT }),
      thing('reservoir', 'RESERVOIR-1'),
      thing('catchment', 'CATCHMENT-7'),
      thing('supportOne', 'support-1'),
      thing('capacity', 'capacity', {
        measureCandidateProperty: 'storedCubicMetres',
        limitSubjectProperty: 'capacityCubicMetres',
        unit: 'm3',
      }),
    ],
    [
      relationship('k1', 'capacity', 'is', 'constraintArchetype'),
      relationship('e1', 'decision', 'about', 'reservoir'),
      relationship('e2', 'decision', 'chose', 'catchment'),
      relationship('e3', 'decision', 'support', 'supportOne'),
      relationship('e4', 'supportOne', 'because', 'capacity'),
    ],
  );
}

describe('useDecisionExplanations', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetAtInstant.mockResolvedValue(thing('reservoir', 'RESERVOIR-1', { capacityCubicMetres: 500 }));
  });

  it('reads every Thing a constraint names against the decision’s own instant', async () => {
    const { result } = renderHook(() => useDecisionExplanations('reservoir', index()));

    await waitFor(() => expect(result.current.settled).toBe(true));
    expect(mockGetAtInstant.mock.calls.map(([id, instant]) => [id, instant])).toEqual([
      ['reservoir', INSTANT],
      ['catchment', INSTANT],
    ]);
    expect(result.current.readings.has(instantReadKey('catchment', INSTANT))).toBe(true);
  });

  it('reads each value once however often the model changes, because a decision cannot be rewritten', async () => {
    const model = index();
    const { result, rerender } = renderHook(() => useDecisionExplanations('reservoir', model));

    await waitFor(() => expect(result.current.settled).toBe(true));
    rerender();
    rerender();

    expect(mockGetAtInstant).toHaveBeenCalledTimes(2);
  });

  it('holds a refused read as answering nothing, so the card says not recorded instead of failing', async () => {
    mockGetAtInstant.mockRejectedValue(new Error('the model is gone'));
    const { result } = renderHook(() => useDecisionExplanations('reservoir', index()));

    await waitFor(() => expect(result.current.settled).toBe(true));
    expect(result.current.readings.get(instantReadKey('catchment', INSTANT))).toBeNull();
  });

  it('asks for nothing where the Thing carries no decision', async () => {
    const { result } = renderHook(() => useDecisionExplanations('capacity', index()));

    await waitFor(() => expect(result.current.settled).toBe(true));
    expect(result.current.decisions).toEqual([]);
    expect(mockGetAtInstant).not.toHaveBeenCalled();
  });

  it('abandons a round when the card closes, so a read it no longer needs cannot land', async () => {
    let handed: AbortSignal | undefined;
    const holding: ((thing: null) => void)[] = [];
    mockGetAtInstant.mockImplementation((_id: string, _instant: string, signal?: AbortSignal) => {
      handed = signal;
      return new Promise((resolve) => holding.push(resolve));
    });

    const { result, unmount } = renderHook(() => useDecisionExplanations('reservoir', index()));
    unmount();

    expect(handed?.aborted).toBe(true);
    await act(async () => {
      holding.forEach((release) => release(null));
    });
    expect(result.current.settled).toBe(false);
  });
});
