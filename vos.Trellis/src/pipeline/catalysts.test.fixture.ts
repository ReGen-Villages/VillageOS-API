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
    | 'intakeConnection' | 'spareConnection' | 'weatherStation' | 'hourlyReading' | 'reportingOffice' | 'dailyDigest'
    | 'runningLow' | 'overdue' | 'reservoir' | 'feeds' | 'readingsArrive' | 'refill' | 'digest'
    | 'readingsStart' | 'readingsEnd' | 'refillStart' | 'digestEnd' | 'standsFor' | 'is' | 'has' | 'binds'
    | 'watcher' | 'watchesRunningLow' | 'runningLowBinding' | 'conductor' | 'surplus' | 'drought' | 'storeSurplus'
    | 'dry' | 'watchesDry' | 'resumes' | 'everyHour' | 'readingsWire' | 'takenOn' | 'success' | 'fail'
    | 'offers' | 'comparesWith' | 'daily' | 'rhythm' | 'weekly' | 'tellKind' | 'followOnKind' | 'stopKind' | 'officeLine',
    string
  >;
}

interface FixtureOptions {
  /** How many services of the orchestrator's archetype the model holds: the page offers a hand-over only where
   *  there is exactly one. */
  orchestrators?: number;
  bindsTellFollowOnAndStop?: boolean;
  bindsRequestedBy?: boolean;
}

export function catalystFixture({ orchestrators = 1, bindsTellFollowOnAndStop = true, bindsRequestedBy = true }: FixtureOptions = {}): CatalystFixture {
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

  const http = thing('http', playing(ROLE.HttpTrigger));
  const graph = thing('graph', playing(ROLE.GraphTrigger));
  const state = thing('state', playing(ROLE.StateTrigger));

  const pipelineArchetype = thing('Workflow', playing(ROLE.Pipeline), true);
  const nodeArchetype = thing('Step', playing(ROLE.PipelineNode), true);
  const inputArchetype = thing('Beginning', playing(ROLE.PipelineInput), true);
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
  const takenOn = thing('firesOn', playing(ROLE.TakenOn));
  const success = thing('Worked', playing(ROLE.Success));
  const fail = thing('Broke', playing(ROLE.Fail));
  thing('Branch', playing(ROLE.DecisionPath), true);
  const offers = thing('proposes', playing(ROLE.Choice));
  const comparesWith = thing('weighs', playing(ROLE.ComparesWith));
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
  const cadencePort = thing('Reader.in', { portName: 'cadence', direction: 'in', type: 'any', required: 'false' });
  relate(cadencePort, is, portArchetype);
  relate(reader, has, cadencePort);
  // The port offers two of a kind; a third of that kind is offered by nothing, for the wider list to find.
  const rhythm = thing('Rhythm', {}, true);
  const daily = thing('Daily');
  const hourly = thing('Hourly');
  const weekly = thing('Weekly');
  for (const each of [daily, hourly, weekly]) relate(each, is, rhythm);
  relate(cadencePort, offers, hourly);
  relate(cadencePort, offers, daily);
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
  const intakeConnection = connection('intake', http, reader, { Subdomain: 'intake' });
  const spareConnection = connection('spare', http, reader, { Subdomain: 'spare' });

  const weatherStation = thing('Weather station');
  relate(weatherStation, is, systemArchetype);
  const hourlyReading = thing('hourly reading');
  relate(hourlyReading, is, kindArchetype);
  relate(weatherStation, sends, hourlyReading);
  relate(hourlyReading, arrivesAt, intakeConnection);

  const reportingOffice = thing('Reporting office');
  relate(reportingOffice, is, systemArchetype);
  const dailyDigest = thing('daily digest');
  relate(dailyDigest, is, kindArchetype);
  relate(reportingOffice, tells, dailyDigest);
  // No trigger, so it adds no row to the rail of catalysts.
  const officeLine = thing('office line');
  relate(officeLine, is, connectionArchetype);
  relate(reportingOffice, thing('reachedThrough', playing(ROLE.ToldThrough)), officeLine);

  const nodeKind = (name: string, roleId: string) => {
    const id = thing(name, bindsTellFollowOnAndStop ? playing(roleId) : {}, true);
    relate(id, is, nodeArchetype);
    return id;
  };
  const tellKind = nodeKind('Herald', ROLE.PipelineTell);
  const followOnKind = nodeKind('Relay', ROLE.PipelineFollowOn);
  const stopKind = nodeKind('Halt', ROLE.PipelineStop);
  for (const [portName, direction, type, required] of [['things', 'in', 'any', 'true'], ['reason', 'in', 'string', 'false'], ['requested', 'out', 'number', 'false']]) {
    const port = thing(`Halt.${portName}`, { portName, direction, type, required });
    relate(port, is, portArchetype);
    relate(stopKind, has, port);
  }
  if (bindsRequestedBy) thing('askedBy', playing(ROLE.RequestedBy));

  const reservoir = thing('Reservoir', {}, true);
  const runningLow = thing('running low');
  relate(runningLow, is, rangeArchetype);
  relate(runningLow, judges, reservoir);
  const overdue = thing('overdue', { EvaluationIntervalSeconds: 60 });
  relate(overdue, is, rangeArchetype);
  relate(overdue, judges, reservoir);

  const watchesRunningLow = connection('watches running low', state, watcher);
  relate(watchesRunningLow, watches, runningLow);
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
  const readingsWire = thing('payload to value', { fromPath: '', toPath: '', itemTransform: '', listTransform: '' });
  relate(readingsWire, is, wireArchetype);
  relate(readingsStart, has, readingsWire);
  relate(readingsWire, carries, readingsEnd);
  relate(readingsWire, inputConnector, portOf.get(readingsStart)!);
  relate(readingsWire, outputConnector, portOf.get(readingsEnd)!);
  const refill = pipeline('Refill');
  const refillStart = boundary(refill, 'A reservoir runs low', 'input', runningLow);
  relate(watchesRunningLow, starts, refill);
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
      intakeConnection, spareConnection, weatherStation, hourlyReading, reportingOffice, dailyDigest,
      runningLow, overdue, reservoir, feeds, readingsArrive, refill, digest,
      readingsStart, readingsEnd, refillStart, digestEnd, standsFor, is, has, binds: BINDS_ID,
      watcher, watchesRunningLow, runningLowBinding: bindings.get(watchesRunningLow)!, conductor, surplus, drought, storeSurplus,
      dry, watchesDry, resumes, everyHour, readingsWire, takenOn, success, fail,
      offers, comparesWith, daily, rhythm, weekly, tellKind, followOnKind, stopKind, officeLine,
    },
  };
}
