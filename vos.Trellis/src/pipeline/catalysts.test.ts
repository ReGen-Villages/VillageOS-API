import { describe, it, expect } from 'vitest';
import { catalystFixture } from './catalysts.test.fixture';
import { catalystRail, outputRail, type CatalystGroupName } from './catalysts';
import { PipelineModel, ROLE } from './model';

describe('the rail of catalysts', () => {
  const { model, id } = catalystFixture();
  const groups = catalystRail(model);
  const group = (name: CatalystGroupName) => groups.find((each) => each.name === name)!.rows;
  const every = groups.flatMap((each) => each.rows);
  const rowFor = (thingId: string) => every.find((each) => each.standsForId === thingId);

  it('holds four groups in order: messages, states, the clock, relationships', () => {
    expect(groups.map((each) => each.name)).toEqual(['messages', 'states', 'clock', 'relationships']);
  });

  it('lists what an external system sends, at the HTTP connection it arrives at, with the pipeline drawn for it', () => {
    const row = group('messages').find((each) => each.who === 'Weather station');
    expect(row).toMatchObject({
      kind: 'message', what: 'hourly reading', standsForId: id.hourlyReading, today: 'Reader',
      starts: { id: id.readingsArrive, name: 'Readings arrive' },
    });
  });

  it('lists an HTTP connection nothing is said to send to, from anybody, saying what reads it today', () => {
    const row = group('messages').find((each) => each.standsForId === id.spareConnection);
    expect(row).toMatchObject({ kind: 'message', who: '', what: 'spare', today: 'Reader' });
    expect(row?.starts).toBeUndefined();
  });

  it('lists what a named system sends before the HTTP connections nobody is said to send to', () => {
    expect(group('messages').map((each) => each.who)).toEqual(['Weather station', '']);
  });

  it('does not list an HTTP connection twice once a kind arriving at it is listed', () => {
    expect(every.filter((each) => each.standsForId === id.intakeConnection)).toEqual([]);
  });

  it('lists a watched state as the kind it judges entering it, with the pipeline the connection starts', () => {
    const row = group('states').find((each) => each.standsForId === id.runningLow);
    expect(row).toMatchObject({
      kind: 'state', who: 'Reservoir', what: 'running low', today: 'Watcher',
      starts: { id: id.refill, name: 'Refill' },
    });
    expect(row?.everySeconds).toBeUndefined();
  });

  it('lists a state the clock re-checks among the states, with its interval', () => {
    const row = group('states').find((each) => each.standsForId === id.overdue);
    expect(row).toMatchObject({ kind: 'state', who: 'Reservoir', what: 'overdue', everySeconds: 60, today: 'Watcher' });
    expect(row?.starts).toBeUndefined();
  });

  it('lists a cadence under the clock, every so many model minutes, with the pipeline drawn from it', () => {
    expect(group('clock')).toEqual([expect.objectContaining({
      kind: 'cadence', who: '', what: 'every hour', standsForId: id.everyHour, everyMinutes: 60, today: '',
      starts: { id: id.tally, name: 'Tally' }, offersOrchestrator: false, placeable: true,
    })]);
    expect(group('clock')[0].handling).toBeUndefined();
  });

  it('lists a connection used as a predicate as a relationship written along it, offering no start node when it names no connection', () => {
    const row = group('relationships').find((each) => each.standsForId === id.feeds);
    expect(row).toMatchObject({ kind: 'relationship', who: '', what: 'feeds', today: '', placeable: false });
  });

  it('says nothing handles a connection used as a predicate that names no connection, since the platform calls nothing for it', () => {
    const row = group('relationships').find((each) => each.standsForId === id.feeds);
    expect(row?.handledBy).toEqual({ by: 'nothing', namesNoConnectionAlong: 'alerts' });
    expect(rowFor(id.balances)?.handledBy).toEqual({ by: 'service', name: 'Balancer' });
  });

  it('names the predicate the model binds to the notified connection role in its own word', () => {
    const fixture = catalystFixture();
    const notifiesId = fixture.model.boundTo(ROLE.NotifiedConnection)!;
    const renamed = fixture.things.map((thing) => (thing.Id === notifiesId ? { ...thing, Name: 'calls on' } : thing));
    const row = catalystRail(new PipelineModel(renamed, fixture.relationships))
      .flatMap((each) => each.rows).find((each) => each.standsForId === fixture.id.feeds);
    expect(row?.handledBy).toEqual({ by: 'nothing', namesNoConnectionAlong: 'calls on' });
  });

  it('lists a predicate naming a connection along notifies once, with the service of the connection it names', () => {
    expect(every.filter((each) => each.standsForId === id.balances)).toEqual([
      expect.objectContaining({ kind: 'relationship', what: 'balances', today: 'Balancer' }),
    ]);
    expect(rowFor(id.balancesCalled)).toBeUndefined();
  });

  it('lists a predicate that is no connection but names one along notifies, offering a start node that Phloem starts from', () => {
    expect(rowFor(id.ripens)).toMatchObject({ kind: 'relationship', what: 'ripens', today: 'Balancer', placeable: true });
    expect(rowFor(id.ripensCalled)).toBeUndefined();
  });

  it('takes no archetype naming a connection along notifies for a relationship written along it', () => {
    expect(group('relationships').map((each) => each.what)).toEqual(['balances', 'feeds', 'ripens']);
  });

  it('leaves out the orchestrator\'s own connections and the connection a system is told through, which no run starts from', () => {
    for (const thingId of [id.resumes, id.stops, id.stopsCalled, id.officeLine]) expect(rowFor(thingId)).toBeUndefined();
  });

  it('orders the states by the kind they judge, then by name', () => {
    expect(group('states').map((each) => each.what)).toEqual(['drought', 'dry', 'overdue', 'running low', 'surplus']);
  });

  it('says a state the orchestrator handles with a drawing is handled by the drawing', () => {
    expect(rowFor(id.surplus)?.handledBy).toEqual({ by: 'drawing', name: 'Store surplus' });
  });

  it('says a message with a drawing is handled by the drawing', () => {
    expect(rowFor(id.hourlyReading)?.handledBy).toEqual({ by: 'drawing', name: 'Readings arrive' });
  });

  it('says a state drawn from but sent elsewhere is handled by the service it is sent to', () => {
    expect(rowFor(id.runningLow)?.handledBy).toEqual({ by: 'service', name: 'Watcher' });
  });

  it('says a state sent to no service is handled by nothing', () => {
    expect(rowFor(id.dry)?.handledBy).toEqual({ by: 'nothing' });
  });

  it('says a state the orchestrator handles with nothing drawn is handled by nothing, since the orchestrator refuses it', () => {
    expect(rowFor(id.drought)?.handledBy).toEqual({ by: 'nothing', refusedByTheOrchestrator: true });
  });

  it('says a cadence with a drawing is handled by the drawing, and one with none by nothing', () => {
    expect(rowFor(id.everyHour)?.handledBy).toEqual({ by: 'drawing', name: 'Tally' });
    const { model: undrawn, id: undrawnId } = catalystFixture({ drawsTheCadence: false });
    expect(catalystRail(undrawn).flatMap((each) => each.rows).find((each) => each.standsForId === undrawnId.everyHour)?.handledBy)
      .toEqual({ by: 'nothing' });
  });

  it('says which connection a state is watched through, what binds it, and that the orchestrator does not handle it', () => {
    const row = rowFor(id.runningLow);
    expect(row?.handling).toEqual({ connectionId: id.watchesRunningLow, bindingId: id.runningLowBinding, byOrchestrator: false });
    expect(row?.offersOrchestrator).toBe(true);
  });

  it('says a state watched through a connection bound to no service has no binding to retract, and offers the orchestrator', () => {
    const row = rowFor(id.dry);
    expect(row).toMatchObject({ today: '', handling: { connectionId: id.watchesDry, byOrchestrator: false }, offersOrchestrator: true });
    expect(row?.handling?.bindingId).toBeUndefined();
  });

  it('says a state the orchestrator handles starts the pipeline drawn from it, and offers nothing more', () => {
    expect(rowFor(id.surplus)).toMatchObject({ handling: { byOrchestrator: true }, offersOrchestrator: false, starts: { id: id.storeSurplus } });
  });

  it('offers the orchestrator on a state the clock re-checks, as on any state', () => {
    expect(rowFor(id.overdue)?.offersOrchestrator).toBe(true);
  });

  it('offers the orchestrator on no message, cadence or relationship, which it cannot start a run from', () => {
    const others = [...group('messages'), ...group('clock'), ...group('relationships')];
    expect(others.length).toBeGreaterThan(0);
    expect(others.every((each) => !each.offersOrchestrator && each.handling === undefined)).toBe(true);
  });

  it('offers the orchestrator nowhere on a model holding no orchestrator, or several', () => {
    for (const orchestrators of [0, 2]) {
      const rows = catalystRail(catalystFixture({ orchestrators }).model).flatMap((each) => each.rows);
      expect(rows.some((each) => each.offersOrchestrator)).toBe(false);
    }
  });

  it('lists nothing on a model that declares no connection and no cadence, and still holds every group', () => {
    const empty = catalystRail(new (model.constructor as typeof import('./model').PipelineModel)([], []));
    expect(empty.map((each) => [each.name, each.rows])).toEqual([['messages', []], ['states', []], ['clock', []], ['relationships', []]]);
  });
});

describe('the rail of outputs', () => {
  const { model, id } = catalystFixture();

  it('offers the answer, every other pipeline to start, every system told through a connection, and stopping runs', () => {
    const rows = outputRail(model, id.readingsArrive);
    expect(rows.map((row) => [row.kind, row.name])).toEqual([
      ['answer', ''],
      ['pipeline', 'Digest'],
      ['pipeline', 'Refill'],
      ['pipeline', 'Store surplus'],
      ['pipeline', 'Tally'],
      ['externalSystem', 'Reporting office'],
      ['stop', ''],
    ]);
    expect(rows.find((row) => row.name === 'Reporting office')?.told).toEqual(['daily digest']);
  });

  it('offers every pipeline while none is open', () => {
    expect(outputRail(model, null).filter((row) => row.kind === 'pipeline').map((row) => row.name))
      .toEqual(['Digest', 'Readings arrive', 'Refill', 'Store surplus', 'Tally']);
  });

  it('offers only the answer on a model that binds none of the tell, follow-on and stop kinds', () => {
    const rows = outputRail(catalystFixture({ bindsTellFollowOnAndStop: false }).model, null);
    expect(rows.map((row) => row.kind)).toEqual(['answer']);
  });

  it('offers no stop node on a model that binds nothing for a stop request to name the run that wrote it, which Phloem refuses', () => {
    const rows = outputRail(catalystFixture({ bindsRequestedBy: false }).model, null);
    expect(rows.map((row) => row.kind)).not.toContain('stop');
  });
});
