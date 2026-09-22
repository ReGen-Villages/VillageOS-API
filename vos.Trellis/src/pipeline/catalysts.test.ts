import { describe, it, expect } from 'vitest';
import { catalystFixture } from './catalystFixture';
import { catalystRail, outputRail } from './catalysts';

describe('the rail of catalysts', () => {
  const { model, id } = catalystFixture();
  const [external, internal] = catalystRail(model);

  it('lists what an external system sends, at the door it arrives at, with the pipeline drawn for it', () => {
    const row = external.rows.find((each) => each.who === 'Weather station');
    expect(row).toMatchObject({
      kind: 'message', what: 'hourly reading', standsForId: id.hourlyReading, today: 'Reader',
      starts: { id: id.readingsArrive, name: 'Readings arrive' },
    });
  });

  it('lists a door nothing is said to send to, from anybody, saying what reads it today', () => {
    const row = external.rows.find((each) => each.standsForId === id.spareDoor);
    expect(row).toMatchObject({ kind: 'message', who: '', what: 'spare', today: 'Reader' });
    expect(row?.starts).toBeUndefined();
  });

  it('does not list a door twice once a kind arriving at it is listed', () => {
    expect(external.rows.filter((each) => each.standsForId === id.intakeDoor)).toEqual([]);
  });

  it('lists a watched state as the kind it judges entering it, with the pipeline the connection starts', () => {
    const row = internal.rows.find((each) => each.standsForId === id.belowReorder);
    expect(row).toMatchObject({
      kind: 'state', who: 'Reservoir', what: 'below reorder', today: 'Watcher',
      starts: { id: id.refill, name: 'Refill' },
    });
    expect(row?.everySeconds).toBeUndefined();
  });

  it('lists a state the clock re-checks under the clock, with its interval', () => {
    const row = internal.rows.find((each) => each.standsForId === id.overdue);
    expect(row).toMatchObject({ kind: 'clock', who: 'Reservoir', what: 'overdue', everySeconds: 60, today: 'Watcher' });
    expect(row?.starts).toBeUndefined();
  });

  it('lists a predicate that is a connection as a relationship written along it', () => {
    const row = internal.rows.find((each) => each.standsForId === id.feeds);
    expect(row).toMatchObject({ kind: 'relationship', who: '', what: 'feeds', today: 'Handler' });
  });

  it('groups the two sides and orders the internal side state, relationship, clock', () => {
    expect(external.side).toBe('external');
    expect(internal.side).toBe('internal');
    expect(internal.rows.map((each) => each.kind)).toEqual(['state', 'relationship', 'clock']);
  });

  it('lists nothing on a model that declares no connection', () => {
    const empty = catalystRail(new (model.constructor as typeof import('./model').PipelineModel)([], []));
    expect(empty.map((group) => group.rows)).toEqual([[], []]);
  });
});

describe('the rail of outputs', () => {
  const { model, id } = catalystFixture();

  it('offers the answer, every other pipeline and every external system with what it is told', () => {
    const rows = outputRail(model, id.readingsArrive);
    expect(rows.map((row) => [row.kind, row.name])).toEqual([
      ['answer', ''],
      ['pipeline', 'Digest'],
      ['pipeline', 'Refill'],
      ['externalSystem', 'Reporting office'],
      ['externalSystem', 'Weather station'],
    ]);
    expect(rows.find((row) => row.name === 'Reporting office')?.told).toEqual(['daily digest']);
    expect(rows.find((row) => row.name === 'Weather station')?.told).toEqual([]);
  });

  it('offers every pipeline while none is open', () => {
    expect(outputRail(model, null).filter((row) => row.kind === 'pipeline').map((row) => row.name))
      .toEqual(['Digest', 'Readings arrive', 'Refill']);
  });
});
