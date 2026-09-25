import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';

const mockGetThingStates = vi.fn();
const mockGetStateTransitions = vi.fn();

vi.mock('../../../api/stateApi', () => ({
  stateApi: {
    getThingStates: (id: string, signal?: AbortSignal) => mockGetThingStates(id, signal),
    getStateTransitions: (id: string, _from?: string, _to?: string, signal?: AbortSignal) =>
      mockGetStateTransitions(id, signal),
  },
}));

const mockGetRelationship = vi.fn();

vi.mock('../../../api/relationshipApi', () => ({
  relationshipApi: {
    get: (id: string, signal?: AbortSignal) => mockGetRelationship(id, signal),
  },
}));

const mockGetAtInstant = vi.fn();

vi.mock('../../../api/thingApi', () => ({
  thingApi: {
    getAtInstant: (id: string, instant: string, signal?: AbortSignal) => mockGetAtInstant(id, instant, signal),
  },
}));

import { buildModelIndex } from '../../../api/dashboardApi';
import type { VosThing, VosRelationship } from '../../../types/vos';
import type { DetailSpecification } from '../../../types/dashboard';
import { formatTimestamp } from '../../../utils/formatters';
import { EntityDetailWindow } from './EntityDetailWindow';

function thing(Id: string, Name: string, Properties: Record<string, unknown> = {}, IsArchetype = false): VosThing {
  return { Id, Name, Properties, IsArchetype };
}
function relationship(Id: string, SubjectId: string, PredicateId: string, TargetId: string): VosRelationship {
  return { Id, Name: Id, SubjectId, PredicateId, TargetId, Properties: {} };
}

/** A catchment feeding a reservoir. The shape a table row opens on. */
function index() {
  return buildModelIndex(
    [
      thing('is', 'is'),
      thing('feeds', 'feeds'),
      thing('catchment', 'CATCHMENT-7', { area: 12.5 }),
      thing('reservoir', 'RESERVOIR-1'),
    ],
    [relationship('r1', 'catchment', 'feeds', 'reservoir')],
  );
}

/** The same catchment, on a relationship the platform dispatches the metering service through. */
function indexWithAService() {
  return buildModelIndex(
    [
      thing('is', 'is'),
      thing('runs', 'runs'),
      thing('connectionArchetype', 'Connection', { __IsConnectionArchetype: true }, true),
      thing('serviceArchetype', 'Service', { __IsServiceArchetype: true }, true),
      thing('measures', 'measures'),
      thing('meteringService', 'metering service'),
      thing('catchment', 'CATCHMENT-7', { area: 12.5 }),
      thing('reservoir', 'RESERVOIR-1'),
    ],
    [
      relationship('w1', 'measures', 'is', 'connectionArchetype'),
      relationship('w2', 'measures', 'runs', 'meteringService'),
      relationship('w3', 'meteringService', 'is', 'serviceArchetype'),
      relationship('e1', 'catchment', 'measures', 'reservoir'),
    ],
  );
}

const DECIDED_AT = '2026-09-22T09:14:03Z';

/**
 * The same catchment, chosen by a sourcing decision about a reservoir: a capacity constraint speaks
 * for it, and the same rule turned CATCHMENT-9 away while counting nine more it did not name. Every
 * value the model holds now is 999, so a number read at the instant cannot be mistaken for a live one.
 */
function indexWithADecision(andAnEarlierOne = false) {
  return buildModelIndex(
    [
      ...(andAnEarlierOne ? [thing('earlier', 'sourcing-0', { decidedAt: '2026-09-21T09:14:03Z' })] : []),
      thing('is', 'is'),
      thing('decisionArchetype', 'Decision', { __IsDecisionArchetype: true }, true),
      thing('constraintArchetype', 'Constraint', { __IsConstraintArchetype: true }, true),
      thing('about', 'about', { __IsDecisionSubjectPredicate: true }),
      thing('chose', 'chose', { __IsChosenPredicate: true }),
      thing('under', 'under', { __IsDecidedUnderPredicate: true }),
      thing('support', 'support', { __IsSupportPredicate: true }),
      thing('refusal', 'refusal', { __IsRefusalPredicate: true }),
      thing('because', 'because', { __IsConsiderationCausePredicate: true }),
      thing('refused', 'refused', { __IsRefusedCandidatePredicate: true }),
      thing('sourcingKind', 'Sourcing'),
      thing('decision', 'sourcing-1', { decidedAt: DECIDED_AT, score: 0.31, candidatesConsidered: 4 }),
      thing('reservoir', 'RESERVOIR-1', { requestedCubicMetres: 999, capacityCubicMetres: 999 }),
      thing('catchment', 'CATCHMENT-7', { area: 12.5, storedCubicMetres: 999 }),
      thing('catchment9', 'CATCHMENT-9', { storedCubicMetres: 999 }),
      thing('rule', 'draw from the fullest catchment'),
      thing('capacity', 'capacity', {
        measureSubjectProperty: 'requestedCubicMetres',
        measureCandidateProperty: 'storedCubicMetres',
        limitSubjectProperty: 'capacityCubicMetres',
        unit: 'm3',
      }),
      thing('supportOne', 'support-1'),
      thing('refusalOne', 'refusal-1', { candidatesRefused: 10 }),
    ],
    [
      relationship('k1', 'sourcingKind', 'is', 'decisionArchetype'),
      relationship('k2', 'decision', 'is', 'sourcingKind'),
      relationship('k3', 'capacity', 'is', 'constraintArchetype'),
      relationship('d1', 'decision', 'about', 'reservoir'),
      relationship('d2', 'decision', 'chose', 'catchment'),
      relationship('d3', 'decision', 'under', 'rule'),
      relationship('d4', 'decision', 'support', 'supportOne'),
      relationship('d5', 'supportOne', 'because', 'capacity'),
      relationship('d6', 'decision', 'refusal', 'refusalOne'),
      relationship('d7', 'refusalOne', 'because', 'rule'),
      relationship('d8', 'refusalOne', 'refused', 'catchment9'),
      ...(andAnEarlierOne ? [relationship('d9', 'earlier', 'about', 'reservoir')] : []),
    ],
  );
}

const SPECIFICATION: DetailSpecification = { relations: [{ predicate: 'feeds', direction: 'out' }] };

function open(overrides: Partial<Parameters<typeof EntityDetailWindow>[0]> = {}) {
  render(
    <EntityDetailWindow
      modelIndex={index()}
      thingId="catchment"
      detail={SPECIFICATION}
      offset={0}
      index={0}
      total={1}
      spreadTick={0}
      zIndex={40}
      onClose={vi.fn()}
      onFocus={vi.fn()}
      onSpread={vi.fn()}
      declaredTypes={{}}
      openDetail={vi.fn()}
      {...overrides}
    />,
  );
}

describe('EntityDetailWindow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetThingStates.mockResolvedValue({ CurrentStates: [] });
    mockGetStateTransitions.mockRejectedValue(new Error('no engine'));
    // jsdom has no pointer capture; the drag handle asks for it on every press.
    Element.prototype.setPointerCapture = () => {};
    Element.prototype.releasePointerCapture = () => {};
    mockGetAtInstant.mockReset().mockImplementation(async (id: string) =>
      id === 'reservoir'
        ? { Id: id, Name: 'RESERVOIR-1', Properties: { requestedCubicMetres: 120, capacityCubicMetres: 500 } }
        : { Id: id, Name: 'CATCHMENT-7', Properties: { storedCubicMetres: 306 } },
    );
    mockGetRelationship.mockReset().mockResolvedValue({
      Id: 'e1',
      SubjectId: 'catchment',
      PredicateId: 'measures',
      TargetId: 'reservoir',
      Properties: {
        __DispatchState: 'Failed',
        __DispatchLastAttemptAt: '2026-07-17T09:41:07Z',
        __DispatchLastError: 'the meter answered nothing',
      },
    });
  });

  it('names every service the platform ran on the Thing, when it ran and how it ended', async () => {
    open({ modelIndex: indexWithAService() });

    expect(await screen.findByText('metering service')).toBeInTheDocument();
    expect(screen.getByText('measures')).toBeInTheDocument();
    expect(screen.getByText('Failed')).toBeInTheDocument();
    expect(screen.getByText('the meter answered nothing')).toBeInTheDocument();
    expect(screen.getByText(formatTimestamp('2026-07-17T09:41:07Z'))).toBeInTheDocument();
  });

  it('opens the service its own card, because a service is a Thing like any other', async () => {
    const openDetail = vi.fn();
    open({ modelIndex: indexWithAService(), openDetail });

    fireEvent.click(await screen.findByText('metering service'));
    expect(openDetail).toHaveBeenCalledWith('meteringService');
  });

  it('says where the record starts when a dispatch carries no time, rather than showing a blank', async () => {
    mockGetRelationship.mockResolvedValue({
      Id: 'e1', SubjectId: 'catchment', PredicateId: 'measures', TargetId: 'reservoir', Properties: {},
    });
    open({ modelIndex: indexWithAService() });

    expect(await screen.findByText('metering service')).toBeInTheDocument();
    expect(screen.getByText(/recorded since the platform loaded this model/i)).toBeInTheDocument();
  });

  it('leaves the section out where no service was dispatched, rather than drawing an empty one', async () => {
    open();
    await screen.findAllByText('CATCHMENT-7');
    expect(screen.queryByText(/handled by/i)).not.toBeInTheDocument();
    expect(mockGetRelationship).not.toHaveBeenCalled();
  });

  it('says what the service chose, the rule it decided under and what spoke for the choice', async () => {
    open({ modelIndex: indexWithADecision(), thingId: 'reservoir' });

    expect(await screen.findByText('Sourcing')).toBeInTheDocument();
    expect(screen.getAllByText('CATCHMENT-7').length).toBeGreaterThan(0);
    expect(screen.getAllByText('draw from the fullest catchment').length).toBeGreaterThan(0);
    expect(screen.getByText('capacity')).toBeInTheDocument();
  });

  it('reads a cited property as it stood at the instant, not as the model holds it now', async () => {
    open({ modelIndex: indexWithADecision(), thingId: 'reservoir' });

    expect(await screen.findByText('426 against 500 m3')).toBeInTheDocument();
    expect(mockGetAtInstant.mock.calls.map(([id, instant]) => [id, instant])).toEqual([
      ['reservoir', DECIDED_AT],
      ['catchment', DECIDED_AT],
    ]);
    expect(screen.queryByText(/1998/)).not.toBeInTheDocument();
  });

  it('says not recorded where the platform answers nothing for that instant, rather than a live value', async () => {
    mockGetAtInstant.mockResolvedValue(null);
    open({ modelIndex: indexWithADecision(), thingId: 'reservoir' });

    expect(await screen.findByText(/not recorded against not recorded m3/i)).toBeInTheDocument();
  });

  it('names the candidates a refusal turned away and the total it counted past its naming bound', async () => {
    open({ modelIndex: indexWithADecision(), thingId: 'reservoir' });

    expect(await screen.findByText('CATCHMENT-9')).toBeInTheDocument();
    expect(screen.getByText('(10 in total)')).toBeInTheDocument();
  });

  it('opens a Thing a decision names its own card, because a cause and a candidate are Things too', async () => {
    const openDetail = vi.fn();
    open({ modelIndex: indexWithADecision(), thingId: 'reservoir', openDetail });

    fireEvent.click(await screen.findByText('capacity'));
    expect(openDetail).toHaveBeenCalledWith('capacity');
  });

  it('says a decision chose nothing, which is itself a decision worth keeping', async () => {
    const model = buildModelIndex(
      [
        thing('about', 'about', { __IsDecisionSubjectPredicate: true }),
        thing('decision', 'sourcing-1', { decidedAt: DECIDED_AT }),
        thing('reservoir', 'RESERVOIR-1'),
      ],
      [relationship('d1', 'decision', 'about', 'reservoir')],
    );
    open({ modelIndex: model, thingId: 'reservoir' });

    expect(await screen.findByText('chose nothing')).toBeInTheDocument();
  });

  it('opens a decision’s own card on the decision it is', async () => {
    open({ modelIndex: indexWithADecision(), thingId: 'decision' });

    expect(await screen.findByText('Sourcing')).toBeInTheDocument();
  });

  it('leaves the section out where nothing decided about the Thing', async () => {
    open();
    await screen.findAllByText('CATCHMENT-7');

    expect(screen.queryByText(/why it was decided/i)).not.toBeInTheDocument();
    expect(mockGetAtInstant).not.toHaveBeenCalled();
  });

  it('opens on the latest decision and keeps the earlier ones one click away, each with its own instant', async () => {
    open({ modelIndex: indexWithADecision(true), thingId: 'reservoir' });

    expect(await screen.findByText('Sourcing')).toBeInTheDocument();
    expect(screen.queryByText('sourcing-0')).not.toBeInTheDocument();

    fireEvent.click(screen.getByText('Earlier decisions (1)'));
    expect(screen.getByText('sourcing-0')).toBeInTheDocument();
    expect(screen.getAllByText(/2026-09-21/).length).toBeGreaterThan(0);
  });
});
