import { describe, it, expect } from 'vitest';
import { catalystFixture } from './catalysts.test.fixture';
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
    expect(internal.rows.map((each) => each.kind)).toEqual(['state', 'state', 'state', 'state', 'relationship', 'clock']);
  });

  it('says a state watched through a connection bound to no service has no binding to retract, and offers the orchestrator', () => {
    const row = internal.rows.find((each) => each.standsForId === id.dry);
    expect(row).toMatchObject({ today: '', handling: { connectionId: id.watchesDry, byOrchestrator: false }, offersOrchestrator: true });
    expect(row?.handling?.bindingId).toBeUndefined();
  });

  it('says which connection a state is watched through, what binds it, and that the orchestrator does not handle it', () => {
    const row = internal.rows.find((each) => each.standsForId === id.belowReorder);
    expect(row?.handling).toEqual({ connectionId: id.watchesReorder, bindingId: id.reorderBinding, byOrchestrator: false });
    expect(row?.offersOrchestrator).toBe(true);
  });

  it('says a state the orchestrator handles starts the pipeline drawn from it, and offers nothing more', () => {
    const row = internal.rows.find((each) => each.standsForId === id.surplus);
    expect(row).toMatchObject({ handling: { byOrchestrator: true }, offersOrchestrator: false, starts: { id: id.storeSurplus } });
  });

  it('says a state the orchestrator handles with nothing drawn from it starts nothing', () => {
    const row = internal.rows.find((each) => each.standsForId === id.drought);
    expect(row).toMatchObject({ handling: { byOrchestrator: true }, offersOrchestrator: false });
    expect(row?.starts).toBeUndefined();
  });

  it('offers the orchestrator on a state the clock re-checks, as on any state', () => {
    expect(internal.rows.find((each) => each.standsForId === id.overdue)?.offersOrchestrator).toBe(true);
  });

  it('offers the orchestrator on no door and no relationship, which it cannot start a run from', () => {
    const others = [...external.rows, ...internal.rows.filter((each) => each.kind === 'relationship')];
    expect(others.length).toBeGreaterThan(0);
    expect(others.every((each) => !each.offersOrchestrator && each.handling === undefined)).toBe(true);
  });

  it('offers the orchestrator nowhere on a model holding no orchestrator, or several', () => {
    for (const orchestrators of [0, 2]) {
      const [, rows] = catalystRail(catalystFixture({ orchestrators }).model);
      expect(rows.rows.some((each) => each.offersOrchestrator)).toBe(false);
    }
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
      ['pipeline', 'Store surplus'],
      ['externalSystem', 'Reporting office'],
      ['externalSystem', 'Weather station'],
    ]);
    expect(rows.find((row) => row.name === 'Reporting office')?.told).toEqual(['daily digest']);
    expect(rows.find((row) => row.name === 'Weather station')?.told).toEqual([]);
  });

  it('offers every pipeline while none is open', () => {
    expect(outputRail(model, null).filter((row) => row.kind === 'pipeline').map((row) => row.name))
      .toEqual(['Digest', 'Readings arrive', 'Refill', 'Store surplus']);
  });
});
