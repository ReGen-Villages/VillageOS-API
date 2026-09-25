import { describe, it, expect } from 'vitest';
import { catalystFixture } from './catalysts.test.fixture';

describe('what a boundary node stands for', () => {
  const { model, id } = catalystFixture();

  it('reads the Thing a node stands for, and its relationship, so a save can replace it', () => {
    expect(model.standsFor(id.readingsStart)).toMatchObject({ thingId: id.hourlyReading });
    expect(model.standsFor(id.readingsStart)?.relationshipId).toMatch(/^r/);
    expect(model.standsFor(id.readingsEnd)).toBeUndefined();
  });

  it('types an end by what it stands for, and says whether a run may come from it or leave it behind', () => {
    expect(model.endInformation(id.hourlyReading)).toEqual({ id: id.hourlyReading, name: 'hourly reading', kind: 'messageKind', mayStart: true, mayEnd: false });
    expect(model.endInformation(id.dailyDigest)).toMatchObject({ kind: 'messageKind', mayStart: false, mayEnd: true });
    expect(model.endInformation(id.weatherStation)).toMatchObject({ kind: 'externalSystem', mayStart: true, mayEnd: true });
    expect(model.endInformation(id.reportingOffice)).toMatchObject({ kind: 'externalSystem', mayStart: false, mayEnd: true });
    expect(model.endInformation(id.intakeDoor)).toMatchObject({ kind: 'door', mayStart: true, mayEnd: false });
    expect(model.endInformation(id.belowReorder)).toMatchObject({ kind: 'state', mayStart: true, mayEnd: false });
    expect(model.endInformation(id.feeds)).toMatchObject({ kind: 'relationship', mayStart: true, mayEnd: false });
    expect(model.endInformation(id.refill)).toMatchObject({ kind: 'pipeline', mayStart: true, mayEnd: true });
    expect(model.endInformation(id.reservoir)).toMatchObject({ kind: 'other', mayStart: false, mayEnd: false });
  });

  it('finds the predicate a stands-for relationship is written through by its mark', () => {
    expect(model.predicateCarrying('__IsStandsForPredicate')).toBe(id.standsFor);
    expect(model.predicateCarrying('__IsNoSuchPredicate')).toBeUndefined();
  });

  it('reads a connection trigger off the connection or the nearest kind above it', () => {
    expect(model.triggerOf(id.intakeDoor)).toBe('http');
    expect(model.triggerOf(id.feeds)).toBe('graph');
    expect(model.triggerOf(id.reservoir)).toBe('');
  });
});
