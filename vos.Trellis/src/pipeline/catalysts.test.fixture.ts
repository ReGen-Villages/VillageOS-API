import type { VosThing, VosRelationship } from '../types/vos';
import { PipelineModel, ROLE } from './model';

/** A model holding one of every catalyst and every output the Pipelines page reads. Every archetype
 *  and predicate is named something the console has never heard of and says what it is through the role
 *  Thing that binds it, so a fixture that resolves at all proves nothing is found by name. */
export interface CatalystFixture {
  model: PipelineModel;
  things: VosThing[];
  relationships: VosRelationship[];
  /** The Things a test names, by the part each plays. */
  id: Record<
    | 'intakeDoor' | 'spareDoor' | 'weatherStation' | 'hourlyReading' | 'reportingOffice' | 'dailyDigest'
    | 'belowReorder' | 'overdue' | 'reservoir' | 'feeds' | 'readingsArrive' | 'refill' | 'digest'
    | 'readingsStart' | 'readingsEnd' | 'refillStart' | 'digestEnd' | 'standsFor' | 'is' | 'has' | 'binds'
    | 'watcher' | 'watchesReorder' | 'reorderBinding' | 'conductor' | 'surplus' | 'drought' | 'storeSurplus'
    | 'dry' | 'watchesDry',
    string
  >;
}

/** How many services of the orchestrator's archetype the model holds: the page offers a hand-over only
 *  where there is exactly one. */
export function catalystFixture({ orchestrators = 1 }: { orchestrators?: number } = {}): CatalystFixture {
  const things: VosThing[] = [];
  const relationships: VosRelationship[] = [];
  let sequence = 0;
  const thing = (name: string, properties: Record<string, unknown> = {}, isArchetype = false): string => {
    const id = `t${++sequence}`;
    things.push({ Id: id, Name: name, Properties: properties, ...(isArchetype ? { IsArchetype: true } : {}) });
    return id;
  };
  const relate = (subject: string, predicate: string, target: string, properties: Record<string, unknown> = {}): string => {
    const id = `r${++sequence}`;
    relationships.push({ Id: id, Name: '', SubjectId: subject, PredicateId: predicate, TargetId: target, Properties: properties });
    return id;
  };
  const is = thing('is'), has = thing('has'), binds = thing('binds');
  const playing = (name: string, roleId: string, isArchetype = false): string => {
    const id = thing(name, {}, isArchetype);
    things.push({ Id: roleId, Name: 'a role', Properties: {} });
    relate(roleId, binds, id);
    return id;
  };

  const triggeredBy = playing('triggeredBy', ROLE.Trigger);
  const watches = playing('watches', ROLE.StateWatch);
  const judges = playing('judges', ROLE.JudgedThing);
  const sends = playing('sends', ROLE.Sends);
  const tells = playing('tells', ROLE.Told);
  const arrivesAt = playing('arrivesAt', ROLE.ArrivesAt);
  const standsFor = playing('standsFor', ROLE.StandsFor);
  const starts = playing('starts', ROLE.PipelineStart);

  const http = thing('http'), graph = thing('graph'), state = thing('state');

  const pipelineArchetype = playing('Workflow', ROLE.Pipeline, true);
  const nodeArchetype = playing('Step', ROLE.PipelineNode, true);
  const inputArchetype = playing('Doorway', ROLE.PipelineInput, true);
  const outputArchetype = playing('Outcome', ROLE.PipelineOutput, true);
  const connectionArchetype = playing('Binding', ROLE.Connection, true);
  const serviceArchetype = playing('Capability', ROLE.Service, true);
  const rangeArchetype = playing('Band', ROLE.Range, true);
  const systemArchetype = playing('Neighbour', ROLE.ExternalSystem, true);
  const kindArchetype = playing('Letter', ROLE.MessageKind, true);
  const wireArchetype = playing('Link', ROLE.PipelineWire, true);
  const portArchetype = playing('Socket', ROLE.Port, true);
  const carries = thing('carries');
  relate(carries, is, wireArchetype);
  relate(inputArchetype, is, nodeArchetype);
  relate(outputArchetype, is, nodeArchetype);

  const service = (name: string) => {
    const id = thing(name);
    relate(id, is, serviceArchetype);
    return id;
  };
  const reader = service('Reader'), watcher = service('Watcher'), handler = service('Handler');
  const orchestratorArchetype = playing('Conductor kind', ROLE.Orchestrator, true);
  relate(orchestratorArchetype, is, serviceArchetype);
  const conductors = Array.from({ length: orchestrators }, (_, index) => {
    const id = thing(index === 0 ? 'Conductor' : `Conductor ${index + 1}`);
    relate(id, is, orchestratorArchetype);
    return id;
  });
  const conductor = conductors[0] ?? handler;

  const bindings = new Map<string, string>();
  const connection = (name: string, trigger: string, boundService: string, properties: Record<string, unknown> = {}) => {
    const id = thing(name, properties);
    relate(id, is, connectionArchetype);
    relate(id, triggeredBy, trigger);
    bindings.set(id, relate(id, has, boundService));
    return id;
  };
  const intakeDoor = connection('intake', http, reader, { Subdomain: 'intake' });
  const spareDoor = connection('spare', http, reader, { Subdomain: 'spare' });

  const weatherStation = thing('Weather station');
  relate(weatherStation, is, systemArchetype);
  const hourlyReading = thing('hourly reading');
  relate(hourlyReading, is, kindArchetype);
  relate(weatherStation, sends, hourlyReading);
  relate(hourlyReading, arrivesAt, intakeDoor);

  const reportingOffice = thing('Reporting office');
  relate(reportingOffice, is, systemArchetype);
  const dailyDigest = thing('daily digest');
  relate(dailyDigest, is, kindArchetype);
  relate(reportingOffice, tells, dailyDigest);

  const reservoir = thing('Reservoir', {}, true);
  const belowReorder = thing('below reorder');
  relate(belowReorder, is, rangeArchetype);
  relate(belowReorder, judges, reservoir);
  const overdue = thing('overdue', { EvaluationIntervalSeconds: 60 });
  relate(overdue, is, rangeArchetype);
  relate(overdue, judges, reservoir);

  const watchesReorder = connection('watches reorder', state, watcher);
  relate(watchesReorder, watches, belowReorder);
  const watchesOverdue = connection('watches overdue', state, watcher);
  relate(watchesOverdue, watches, overdue);
  const feeds = connection('feeds', graph, handler);

  const surplus = thing('surplus');
  relate(surplus, is, rangeArchetype);
  relate(surplus, judges, reservoir);
  relate(connection('watches surplus', state, conductor), watches, surplus);
  const drought = thing('drought');
  relate(drought, is, rangeArchetype);
  relate(drought, judges, reservoir);
  relate(connection('watches drought', state, conductor), watches, drought);
  const dry = thing('dry');
  relate(dry, is, rangeArchetype);
  relate(dry, judges, reservoir);
  const watchesDry = thing('watches dry');
  relate(watchesDry, is, connectionArchetype);
  relate(watchesDry, triggeredBy, state);
  relate(watchesDry, watches, dry);
  relate(watchesDry, has, thing('a note on dry spells'));

  const pipeline = (name: string) => {
    const id = thing(name);
    relate(id, is, pipelineArchetype);
    return id;
  };
  const boundary = (pipelineId: string, name: string, end: 'input' | 'output', standingFor?: string) => {
    const id = thing(name, { x: 0, y: 0 });
    relate(id, is, nodeArchetype);
    relate(id, is, end === 'input' ? inputArchetype : outputArchetype);
    relate(pipelineId, has, id);
    if (standingFor) relate(id, standsFor, standingFor);
    const direction = end === 'input' ? 'out' : 'in';
    const port = thing(`${name}.${direction}`, { portName: end === 'input' ? 'payload' : 'value', direction, type: 'any', required: String(end === 'output') });
    relate(port, is, portArchetype);
    relate(id, has, port);
    return id;
  };

  const readingsArrive = pipeline('Readings arrive');
  const readingsStart = boundary(readingsArrive, 'A reading arrives', 'input', hourlyReading);
  const readingsEnd = boundary(readingsArrive, 'The answer', 'output');
  relate(readingsStart, carries, readingsEnd, { fromPort: 'payload', toPort: 'value' });
  const refill = pipeline('Refill');
  const refillStart = boundary(refill, 'A reservoir runs low', 'input', belowReorder);
  relate(watchesReorder, starts, refill);
  const digest = pipeline('Digest');
  const digestEnd = boundary(digest, 'The office is told', 'output', reportingOffice);
  const storeSurplus = pipeline('Store surplus');
  boundary(storeSurplus, 'A reservoir overflows', 'input', surplus);

  return {
    model: new PipelineModel(things, relationships),
    things,
    relationships,
    id: {
      intakeDoor, spareDoor, weatherStation, hourlyReading, reportingOffice, dailyDigest,
      belowReorder, overdue, reservoir, feeds, readingsArrive, refill, digest,
      readingsStart, readingsEnd, refillStart, digestEnd, standsFor, is, has, binds,
      watcher, watchesReorder, reorderBinding: bindings.get(watchesReorder)!, conductor, surplus, drought, storeSurplus,
      dry, watchesDry,
    },
  };
}
