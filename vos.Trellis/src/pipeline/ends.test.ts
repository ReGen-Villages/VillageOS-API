import { describe, it, expect } from 'vitest';
import { catalystFixture } from './catalysts.test.fixture';
import { PipelineModel, ROLE } from './model';

function triggerOfConnection({ model, relationships }: ReturnType<typeof catalystFixture>, connectionId: string): string {
  const triggeredBy = model.boundTo(ROLE.Trigger);
  return relationships.find((r) => r.SubjectId === connectionId && r.PredicateId === triggeredBy)!.TargetId;
}

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
    expect(model.endInformation(id.weatherStation)).toMatchObject({ kind: 'externalSystem', mayStart: true, mayEnd: false });
    expect(model.endInformation(id.reportingOffice)).toMatchObject({ kind: 'externalSystem', mayStart: false, mayEnd: false });
    expect(model.endInformation(id.intakeDoor)).toMatchObject({ kind: 'door', mayStart: true, mayEnd: false });
    expect(model.endInformation(id.belowReorder)).toMatchObject({ kind: 'state', mayStart: true, mayEnd: false });
    expect(model.endInformation(id.feeds)).toMatchObject({ kind: 'relationship', mayStart: true, mayEnd: false });
    expect(model.endInformation(id.refill)).toMatchObject({ kind: 'pipeline', mayStart: true, mayEnd: false });
    expect(model.endInformation(id.everyHour)).toMatchObject({ kind: 'cadence', mayStart: true, mayEnd: false });
    expect(model.endInformation(id.reservoir)).toMatchObject({ kind: 'other', mayStart: false, mayEnd: false });
  });

  it('finds the predicate a stands-for relationship is written through by the role that binds it', () => {
    expect(model.boundTo(ROLE.StandsFor)).toBe(id.standsFor);
    expect(model.boundTo('a role nothing binds')).toBeUndefined();
  });

  it('reads a connection trigger off the connection or the nearest kind above it', () => {
    expect(model.triggerOf(id.intakeDoor)).toBe('http');
    expect(model.triggerOf(id.feeds)).toBe('graph');
    expect(model.triggerOf(id.reservoir)).toBe('');
  });
});

describe('a trigger the model names in its own words', () => {
  it.each([
    ['intakeDoor', 'http', 'door'],
    ['feeds', 'graph', 'relationship'],
    ['watchesDry', 'state', 'state'],
  ] as const)('reads %s by the role its trigger plays, not by the trigger\'s name', (connection, trigger, kind) => {
    const fixture = catalystFixture();
    const triggerId = triggerOfConnection(fixture, fixture.id[connection]);
    const renamed = fixture.things.map((t) => (t.Id === triggerId ? { ...t, Name: 'named in the model\'s own words' } : t));
    const model = new PipelineModel(renamed, fixture.relationships);

    expect(model.triggerOf(fixture.id[connection])).toBe(trigger);
    expect(model.endInformation(fixture.id[connection])).toMatchObject({ kind, mayStart: true });
  });

  it('starts nothing from a connection whose trigger no trigger role binds, whatever the trigger is called', () => {
    const fixture = catalystFixture();
    const triggerId = triggerOfConnection(fixture, fixture.id.feeds);
    const unbound = fixture.relationships.filter((r) => !(r.PredicateId === fixture.id.binds && r.TargetId === triggerId));
    const model = new PipelineModel(fixture.things, unbound);

    expect(model.triggerOf(fixture.id.feeds)).toBe('');
    expect(model.endInformation(fixture.id.feeds)).toMatchObject({ kind: 'other', mayStart: false });
  });
});
