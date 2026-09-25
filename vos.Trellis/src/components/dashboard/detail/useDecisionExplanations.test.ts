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
import { decisionsOn, instantReadKey } from './decisionExplanation';

function thing(Id: string, Name: string, Properties: Record<string, unknown> = {}, IsArchetype = false): VosThing {
  return { Id, Name, Properties, IsArchetype };
}
function relationship(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, Name: Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

const INSTANT = '2026-09-22T09:14:03Z';
const EARLIER = '2026-09-21T09:14:03Z';

/** A reservoir decided about once, or twice where an earlier decision is asked for. Each decision
 *  reads a property off the subject and one off what it chose. */
function decisions(andAnEarlierOne = false) {
  const index = buildModelIndex(
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
      ...(andAnEarlierOne
        ? [
            thing('earlier', 'sourcing-0', { decidedAt: EARLIER }),
            thing('supportZero', 'support-0'),
            thing('springs', 'SPRING-2'),
          ]
        : []),
    ],
    [
      relationship('k1', 'capacity', 'is', 'constraintArchetype'),
      relationship('e1', 'decision', 'about', 'reservoir'),
      relationship('e2', 'decision', 'chose', 'catchment'),
      relationship('e3', 'decision', 'support', 'supportOne'),
      relationship('e4', 'supportOne', 'because', 'capacity'),
      ...(andAnEarlierOne
        ? [
            relationship('e5', 'earlier', 'about', 'reservoir'),
            relationship('e6', 'earlier', 'chose', 'springs'),
            relationship('e7', 'earlier', 'support', 'supportZero'),
            relationship('e8', 'supportZero', 'because', 'capacity'),
          ]
        : []),
    ],
  );
  return decisionsOn('reservoir', index);
}

describe('useDecisionExplanations', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetAtInstant.mockImplementation(async (id: string) => thing(id, id, { capacityCubicMetres: 500 }));
  });

  it('reads every Thing a constraint names against the decision’s own instant', async () => {
    const { result } = renderHook(() => useDecisionExplanations(decisions()));

    await waitFor(() => expect(result.current.settled).toBe(true));
    expect(mockGetAtInstant.mock.calls.map(([id, instant]) => [id, instant])).toEqual([
      ['reservoir', INSTANT],
      ['catchment', INSTANT],
    ]);
  });

  it('reads each value once however often the model changes, because a decision cannot be rewritten', async () => {
    const asked = decisions();
    const { result, rerender } = renderHook(() => useDecisionExplanations(asked));

    await waitFor(() => expect(result.current.settled).toBe(true));
    rerender();
    rerender();

    expect(mockGetAtInstant).toHaveBeenCalledTimes(2);
  });

  it('reads only what an earlier decision adds when it is opened, and keeps what it already holds', async () => {
    const both = decisions(true);
    const { result, rerender } = renderHook(({ shown }) => useDecisionExplanations(shown), {
      initialProps: { shown: [both[0]] },
    });

    await waitFor(() => expect(result.current.settled).toBe(true));
    expect(mockGetAtInstant).toHaveBeenCalledTimes(2);

    rerender({ shown: both });
    await waitFor(() => expect(result.current.settled).toBe(true));

    // The subject is read again because the earlier decision names a different instant; what the
    // later decision already answered is not asked for a second time.
    expect(mockGetAtInstant.mock.calls.map(([id, instant]) => [id, instant])).toEqual([
      ['reservoir', INSTANT],
      ['catchment', INSTANT],
      ['reservoir', EARLIER],
      ['springs', EARLIER],
    ]);
    expect(result.current.readings.get(instantReadKey('catchment', INSTANT))).not.toBeUndefined();
  });

  it('holds a refused read as answering nothing, so the card says not recorded instead of failing', async () => {
    mockGetAtInstant.mockRejectedValue(new Error('the model is gone'));
    const { result } = renderHook(() => useDecisionExplanations(decisions()));

    await waitFor(() => expect(result.current.settled).toBe(true));
    expect(result.current.readings.get(instantReadKey('catchment', INSTANT))).toBeNull();
  });

  it('asks for nothing where no decision names a property to read', async () => {
    const { result } = renderHook(() => useDecisionExplanations([]));

    expect(result.current.settled).toBe(true);
    expect(mockGetAtInstant).not.toHaveBeenCalled();
  });

  it('abandons a round when the card closes, so a read it no longer needs cannot land', async () => {
    let handed: AbortSignal | undefined;
    const holding: ((thing: null) => void)[] = [];
    mockGetAtInstant.mockImplementation((_id: string, _instant: string, signal?: AbortSignal) => {
      handed = signal;
      return new Promise((resolve) => holding.push(resolve));
    });

    const { result, unmount } = renderHook(() => useDecisionExplanations(decisions()));
    unmount();

    expect(handed?.aborted).toBe(true);
    await act(async () => {
      holding.forEach((release) => release(null));
    });
    expect(result.current.settled).toBe(false);
  });
});

describe('what a card holds while it is open', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetAtInstant.mockImplementation(async (id: string) => thing(id, id, { capacityCubicMetres: 500 }));
  });

  it('lets go of what a decision no longer on screen cited, rather than holding every read forever', async () => {
    const both = decisions(true);
    const { result, rerender } = renderHook(({ shown }) => useDecisionExplanations(shown), {
      initialProps: { shown: [both[0]] },
    });

    await waitFor(() => expect(result.current.settled).toBe(true));
    expect(result.current.readings.has(instantReadKey('catchment', INSTANT))).toBe(true);

    rerender({ shown: [both[1]] });
    await waitFor(() => expect(result.current.settled).toBe(true));

    expect(result.current.readings.has(instantReadKey('catchment', INSTANT))).toBe(false);
    expect(result.current.readings.has(instantReadKey('springs', EARLIER))).toBe(true);
  });
});
