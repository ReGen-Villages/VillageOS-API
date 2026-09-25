import type { VosThing, VosRelationship } from '../types/vos';
import { PipelineModel, ARCHETYPE_FLAG, PREDICATE_FLAG } from './model';

/** A model holding one of every catalyst and every output the Pipelines page reads. Every archetype
 *  and predicate is named something the console has never heard of and says what it is by the mark
 *  it carries, so a fixture that resolves at all proves nothing is found by name. */
export interface CatalystFixture {
  model: PipelineModel;
  things: VosThing[];
  relationships: VosRelationship[];
  /** The Things a test names, by the part each plays. */
  id: Record<
    | 'intakeDoor' | 'spareDoor' | 'weatherStation' | 'hourlyReading' | 'reportingOffice' | 'dailyDigest'
    | 'belowReorder' | 'overdue' | 'reservoir' | 'feeds' | 'readingsArrive' | 'refill' | 'digest'
    | 'readingsStart' | 'readingsEnd' | 'refillStart' | 'digestEnd' | 'standsFor' | 'is' | 'has',
    string
  >;
}

export function catalystFixture(): CatalystFixture {
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
  const marked = (flag: string) => ({ [flag]: true });

  const is = thing('is'), has = thing('has');
  const triggeredBy = thing('triggeredBy', marked(PREDICATE_FLAG.Trigger));
  const watches = thing('watches', marked(PREDICATE_FLAG.StateWatch));
  const judges = thing('judges', marked(PREDICATE_FLAG.JudgedThing));
  const sends = thing('sends', marked(PREDICATE_FLAG.Sends));
  const tells = thing('tells', marked(PREDICATE_FLAG.Told));
  const arrivesAt = thing('arrivesAt', marked(PREDICATE_FLAG.ArrivesAt));
  const standsFor = thing('standsFor', marked(PREDICATE_FLAG.StandsFor));
  const starts = thing('starts', marked(PREDICATE_FLAG.PipelineStart));

  const http = thing('http'), graph = thing('graph'), state = thing('state');

  const pipelineArchetype = thing('Workflow', marked(ARCHETYPE_FLAG.Pipeline), true);
  const nodeArchetype = thing('Step', marked(ARCHETYPE_FLAG.PipelineNode), true);
  const inputArchetype = thing('Doorway', marked(ARCHETYPE_FLAG.PipelineInput), true);
  const outputArchetype = thing('Outcome', marked(ARCHETYPE_FLAG.PipelineOutput), true);
  const connectionArchetype = thing('Binding', marked(ARCHETYPE_FLAG.Connection), true);
  const serviceArchetype = thing('Capability', marked(ARCHETYPE_FLAG.Service), true);
  const rangeArchetype = thing('Band', marked(ARCHETYPE_FLAG.Range), true);
  const systemArchetype = thing('Neighbour', marked(ARCHETYPE_FLAG.ExternalSystem), true);
  const kindArchetype = thing('Letter', marked(ARCHETYPE_FLAG.MessageKind), true);
  const wireArchetype = thing('Link', marked(ARCHETYPE_FLAG.PipelineWire), true);
  const portArchetype = thing('Socket', marked(ARCHETYPE_FLAG.Port), true);
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

  const connection = (name: string, trigger: string, boundService: string, properties: Record<string, unknown> = {}) => {
    const id = thing(name, properties);
    relate(id, is, connectionArchetype);
    relate(id, triggeredBy, trigger);
    relate(id, has, boundService);
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

  return {
    model: new PipelineModel(things, relationships),
    things,
    relationships,
    id: {
      intakeDoor, spareDoor, weatherStation, hourlyReading, reportingOffice, dailyDigest,
      belowReorder, overdue, reservoir, feeds, readingsArrive, refill, digest,
      readingsStart, readingsEnd, refillStart, digestEnd, standsFor, is, has,
    },
  };
}
