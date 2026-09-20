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
});
