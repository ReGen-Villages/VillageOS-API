import { ARCHETYPE_FLAG, type PipelineModel, type PipelineNamed, type CatalystConnection } from './model';

// The two rails beside the canvas, read off the model's own wiring and never written from memory: what
// sets a run off, and what a run may leave behind. A site that adds a system, a state or a connection
// adds a row the same way.

/** The kinds of catalyst the platform dispatches on: a message arriving at a door, a Thing entering a
 *  state, a relationship written along a predicate, and a state the clock re-checks on an interval. */
export type CatalystKind = 'message' | 'state' | 'relationship' | 'clock';

export interface CatalystRow {
  id: string;
  kind: CatalystKind;
  /** Who or what the catalyst is about: the sending system, or the kind of Thing entering the state.
   *  Empty for a door nobody in particular is said to send to, or a predicate. */
  who: string;
  /** The act: the kind of message, the state, the predicate. */
  what: string;
  /** The Thing a start node placed from this row stands for. */
  standsForId: string;
  /** The pipeline drawn for it, where one is. */
  starts?: PipelineNamed;
  /** What happens to it today: the service the platform dispatches. Empty where the model says nothing. */
  today: string;
  /** For a state the clock re-checks: how often. */
  everySeconds?: number;
}

export interface CatalystGroup {
  side: 'external' | 'internal';
  rows: CatalystRow[];
}

const KIND_ORDER: CatalystKind[] = ['message', 'state', 'relationship', 'clock'];

function ordered(rows: CatalystRow[]): CatalystRow[] {
  return rows.sort((a, b) => KIND_ORDER.indexOf(a.kind) - KIND_ORDER.indexOf(b.kind)
    || a.who.localeCompare(b.who) || a.what.localeCompare(b.what));
}

/** Everything that sets a run off, in two groups: what arrives from outside, and what the model does on
 *  its own. Each is one pass over the model. */
export function catalystRail(model: PipelineModel): [CatalystGroup, CatalystGroup] {
  const connections = model.catalystConnections();
  const byId = new Map(connections.map((connection) => [connection.connectionId, connection]));
  const external: CatalystRow[] = [];
  const internal: CatalystRow[] = [];

  const startedBy = (thingId: string) => model.pipelineWhoseEndStandsFor(thingId, 'input');
  const startedAt = (door: CatalystConnection | undefined) => door && (startedBy(door.connectionId) ?? door.startsPipeline);

  const doorsCovered = new Set<string>();
  for (const system of model.externalSystems())
    for (const kind of system.sends) {
      const door = kind.arrivesAtConnectionId ? byId.get(kind.arrivesAtConnectionId) : undefined;
      if (door) doorsCovered.add(door.connectionId);
      const starts = startedBy(kind.id) ?? startedBy(system.id) ?? startedAt(door);
      external.push({
        id: `message:${system.id}:${kind.id}`, kind: 'message', who: system.name, what: kind.name,
        standsForId: kind.id, ...(starts ? { starts } : {}), today: door?.serviceName ?? '',
      });
    }

  const seenRanges = new Set<string>();
  for (const connection of connections) {
    if (connection.trigger === 'http') {
      if (doorsCovered.has(connection.connectionId)) continue;
      const starts = startedAt(connection);
      external.push({
        id: `door:${connection.connectionId}`, kind: 'message', who: '', what: connection.subdomain || connection.name,
        standsForId: connection.connectionId, ...(starts ? { starts } : {}), today: connection.serviceName,
      });
    } else if (connection.trigger === 'graph') {
      const starts = startedBy(connection.connectionId) ?? connection.startsPipeline;
      internal.push({
        id: `relationship:${connection.connectionId}`, kind: 'relationship', who: '', what: connection.name,
        standsForId: connection.connectionId, ...(starts ? { starts } : {}), today: connection.serviceName,
      });
    } else if (connection.watches) {
      // A state several connections watch is one row: drawn if any of them draws.
      const { rangeId, name, kindJudged, everySeconds } = connection.watches;
      if (seenRanges.has(rangeId)) continue;
      seenRanges.add(rangeId);
      const watching = connections.filter((each) => each.watches?.rangeId === rangeId);
      const starts = startedBy(rangeId) ?? watching.find((each) => each.startsPipeline)?.startsPipeline;
      internal.push({
        id: `state:${rangeId}`, kind: everySeconds > 0 ? 'clock' : 'state', who: kindJudged, what: name,
        standsForId: rangeId, ...(starts ? { starts } : {}), today: watching.find((each) => each.serviceName)?.serviceName ?? '',
        ...(everySeconds > 0 ? { everySeconds } : {}),
      });
    }
  }

  return [{ side: 'external', rows: ordered(external) }, { side: 'internal', rows: ordered(internal) }];
}

export type OutputKind = 'answer' | 'pipeline' | 'externalSystem';

export interface OutputRow {
  id: string;
  kind: OutputKind;
  name: string;
  /** For an external system: the kinds of message it is told. */
  told?: string[];
}

/** What a run may leave behind: the answer to whatever started it, another pipeline, and the external
 *  systems that may be told. The open pipeline is not offered to itself. */
export function outputRail(model: PipelineModel, openedPipelineId: string | null): OutputRow[] {
  const pipelines = model.thingsOfArchetypeCarrying(ARCHETYPE_FLAG.Pipeline)
    .filter((pipeline) => pipeline.Id !== openedPipelineId)
    .map((pipeline) => ({ id: pipeline.Id, kind: 'pipeline' as const, name: pipeline.Name }))
    .sort((a, b) => a.name.localeCompare(b.name));
  const systems = model.externalSystems()
    .map((system) => ({ id: system.id, kind: 'externalSystem' as const, name: system.name, told: system.told.map((kind) => kind.name) }));
  return [{ id: 'answer', kind: 'answer', name: '' }, ...pipelines, ...systems];
}
