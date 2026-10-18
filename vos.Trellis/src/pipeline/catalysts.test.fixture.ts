import type { VosThing, VosRelationship } from '../types/vos';
import { PipelineModel, ROLE } from './model';
import { BINDS_ID, playing, withRoles } from './roles.test.fixture';

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
    | 'dry' | 'watchesDry' | 'resumes' | 'everyHour',
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
  const is = thing('is'), has = thing('has');

  const triggeredBy = thing('triggeredBy', playing(ROLE.Trigger));
  const watches = thing('watches', playing(ROLE.StateWatch));
  const judges = thing('judges', playing(ROLE.JudgedThing));
  const sends = thing('sends', playing(ROLE.Sends));
  const tells = thing('tells', playing(ROLE.Told));
  const arrivesAt = thing('arrivesAt', playing(ROLE.ArrivesAt));
  const standsFor = thing('standsFor', playing(ROLE.StandsFor));
  const starts = thing('starts', playing(ROLE.PipelineStart));

  const http = thing('http'), graph = thing('graph'), state = thing('state');

  const pipelineArchetype = thing('Workflow', playing(ROLE.Pipeline), true);
  const nodeArchetype = thing('Step', playing(ROLE.PipelineNode), true);
  const inputArchetype = thing('Doorway', playing(ROLE.PipelineInput), true);
  const outputArchetype = thing('Outcome', playing(ROLE.PipelineOutput), true);
  const connectionArchetype = thing('Binding', playing(ROLE.Connection), true);
  const serviceArchetype = thing('Capability', playing(ROLE.Service), true);
  const rangeArchetype = thing('Band', playing(ROLE.Range), true);
  const systemArchetype = thing('Neighbour', playing(ROLE.ExternalSystem), true);
  const kindArchetype = thing('Letter', playing(ROLE.MessageKind), true);
  const wireArchetype = thing('Link', playing(ROLE.PipelineWire), true);
  const portArchetype = thing('Socket', playing(ROLE.Port), true);
  const carries = thing('carries');
  relate(carries, is, wireArchetype);
  const inputConnector = thing('takesFrom', playing(ROLE.InputConnector));
  const outputConnector = thing('deliversTo', playing(ROLE.OutputConnector));
  const everyHour = thing('every hour');
  relate(everyHour, is, thing('Metronome', playing(ROLE.Cadence), true));
  relate(inputArchetype, is, nodeArchetype);
  relate(outputArchetype, is, nodeArchetype);

  const service = (name: string) => {
    const id = thing(name);
    relate(id, is, serviceArchetype);
    return id;
  };
  const reader = service('Reader'), watcher = service('Watcher'), handler = service('Handler');
  const orchestratorArchetype = thing('Conductor kind', playing(ROLE.Orchestrator), true);
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
  const resumes = connection('resumes', graph, conductor, playing(ROLE.RunResumption));

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
  const portOf = new Map<string, string>();
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
    portOf.set(id, port);
    return id;
  };

  const readingsArrive = pipeline('Readings arrive');
  const readingsStart = boundary(readingsArrive, 'A reading arrives', 'input', hourlyReading);
  const readingsEnd = boundary(readingsArrive, 'The answer', 'output');
  const readingsWire = thing('payload to value', { fromPath: '', toPath: '', transform: '' });
  relate(readingsWire, is, wireArchetype);
  relate(readingsStart, has, readingsWire);
  relate(readingsWire, carries, readingsEnd);
  relate(readingsWire, inputConnector, portOf.get(readingsStart)!);
  relate(readingsWire, outputConnector, portOf.get(readingsEnd)!);
  const refill = pipeline('Refill');
  const refillStart = boundary(refill, 'A reservoir runs low', 'input', belowReorder);
  relate(watchesReorder, starts, refill);
  const digest = pipeline('Digest');
  const digestEnd = boundary(digest, 'The office is told', 'output', reportingOffice);
  const storeSurplus = pipeline('Store surplus');
  boundary(storeSurplus, 'A reservoir overflows', 'input', surplus);

  const [held, heldRelationships] = withRoles(things, relationships);
  return {
    model: new PipelineModel(held, heldRelationships),
    things: held,
    relationships: heldRelationships,
    id: {
      intakeDoor, spareDoor, weatherStation, hourlyReading, reportingOffice, dailyDigest,
      belowReorder, overdue, reservoir, feeds, readingsArrive, refill, digest,
      readingsStart, readingsEnd, refillStart, digestEnd, standsFor, is, has, binds: BINDS_ID,
      watcher, watchesReorder, reorderBinding: bindings.get(watchesReorder)!, conductor, surplus, drought, storeSurplus,
      dry, watchesDry, resumes, everyHour,
    },
  };
}
