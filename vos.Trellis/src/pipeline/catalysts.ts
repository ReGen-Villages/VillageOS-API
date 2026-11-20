import { ROLE, type PipelineModel, type PipelineNamed, type CatalystConnection } from './model';

// The two rails beside the canvas, read off the model's own wiring and never written from memory: what
// sets a run off, and what a run may leave behind. A site that adds a system, a state or a connection
// adds a row the same way.

/** The kinds of catalyst: a message arriving at an HTTP connection, a Thing entering a state, a cadence coming
 *  due, and a relationship written along a predicate. */
type CatalystKind = 'message' | 'state' | 'cadence' | 'relationship';

export type CatalystGroupName = 'messages' | 'states' | 'clock' | 'relationships';

/** What the model does with a catalyst today: runs a drawing, calls a service, or nothing. A state the orchestrator
 *  receives with nothing drawn from it is refused each time, so nothing runs; a relationship along a connection that
 *  names no connection along notifies is called for by nothing. */
type CatalystHandling =
  | { by: 'drawing'; name: string }
  | { by: 'service'; name: string }
  | { by: 'nothing'; refusedByTheOrchestrator?: true; namesNoConnectionAlong?: string };

export interface CatalystRow {
  id: string;
  kind: CatalystKind;
  /** Who or what the catalyst is about: the sending system, or the kind of Thing entering the state.
   *  Empty for an HTTP connection nobody in particular is said to send to, a cadence, or a predicate. */
  who: string;
  /** The act: the kind of message, the state, the cadence, the predicate. */
  what: string;
  /** The Thing a start node placed from this row stands for. */
  standsForId: string;
  /** Whether a start node standing for it may start a run, as the orchestrator reads a pipeline's start. */
  placeable: boolean;
  /** The pipeline drawn for it, where one is. */
  starts?: PipelineNamed;
  /** The service the platform calls for it. Empty where the model names none. */
  today: string;
  handledBy: CatalystHandling;
  /** For a state the clock re-checks: how often. */
  everySeconds?: number;
  /** For a cadence: how many model minutes apart its runs start. */
  everyMinutes?: number;
  /** For a state watched through one connection: what the broker sends its entries to. */
  handling?: StateHandling;
  /** For a connection used as a predicate that the platform calls nothing for: the model's name for the predicate it would
   *  have to name a connection along. */
  namesNoConnectionAlong?: string;
  /** Whether the row may be handed to the orchestrator: a state it does not handle yet, in a model holding
   *  exactly one. Never anything else, which reaches the orchestrator without naming the drawing. */
  offersOrchestrator: boolean;
}

interface StateHandling {
  connectionId: string;
  /** The `has` relationship binding the connection to its service, which a hand-over retracts. */
  bindingId?: string;
  /** Whether the orchestrator receives the entries, so a pipeline drawn from the state is what runs. */
  byOrchestrator: boolean;
}

export interface CatalystGroup {
  name: CatalystGroupName;
  rows: CatalystRow[];
}

type RowWithoutItsHandling = Omit<CatalystRow, 'handledBy' | 'placeable'>;

// A state drawn from runs its drawing only when the orchestrator receives its entries; sent anywhere else, the
// drawing never runs, so the service it is sent to is what happens.
function handledBy(row: RowWithoutItsHandling): CatalystHandling {
  if (row.namesNoConnectionAlong !== undefined) return { by: 'nothing', namesNoConnectionAlong: row.namesNoConnectionAlong };
  if (row.starts && (row.handling === undefined || row.handling.byOrchestrator)) return { by: 'drawing', name: row.starts.name };
  if (row.handling?.byOrchestrator) return { by: 'nothing', refusedByTheOrchestrator: true };
  return row.today ? { by: 'service', name: row.today } : { by: 'nothing' };
}

function ordered(rows: CatalystRow[]): CatalystRow[] {
  return rows.sort((a, b) => Number(a.who === '') - Number(b.who === '')
    || a.who.localeCompare(b.who) || a.what.localeCompare(b.what));
}

/** Everything that sets a run off, in groups: messages from outside, states the model enters, the clock, and
 *  relationships written. Every group is returned, empty or not. The orchestrator's own connections and a
 *  connection a system is told through are left out, since no run starts from them. */
export function catalystRail(model: PipelineModel): CatalystGroup[] {
  const leftOut = new Set([
    model.boundTo(ROLE.RunResumption), model.boundTo(ROLE.RunStopping),
    ...model.externalSystems().map((system) => system.toldThroughConnectionId),
  ].filter((thingId): thingId is string => thingId !== undefined));
  const notifying = model.predicatesNotifying();
  const notifiesName = model.thing(model.boundTo(ROLE.NotifiedConnection) ?? '')?.Name ?? '';
  for (const { predicate, connectionId } of notifying) if (leftOut.has(predicate.Id)) leftOut.add(connectionId);
  const connections = model.catalystConnections().filter((connection) => !leftOut.has(connection.connectionId));
  const byId = new Map(connections.map((connection) => [connection.connectionId, connection]));
  const orchestratorId = model.orchestratorService()?.Id;
  const rows: Record<CatalystGroupName, CatalystRow[]> = { messages: [], states: [], clock: [], relationships: [] };
  const add = (name: CatalystGroupName, row: RowWithoutItsHandling) => rows[name].push({
    ...row, handledBy: handledBy(row), placeable: model.endInformation(row.standsForId).mayStart,
  });

  const startedBy = (thingId: string) => model.pipelineWhoseEndStandsFor(thingId, 'input');
  const startedAt = (arrivesAt: CatalystConnection | undefined) =>
    arrivesAt && (startedBy(arrivesAt.connectionId) ?? arrivesAt.startsPipeline);

  const connectionsCovered = new Set<string>();
  for (const system of model.externalSystems())
    for (const kind of system.sends) {
      const arrivesAt = kind.arrivesAtConnectionId ? byId.get(kind.arrivesAtConnectionId) : undefined;
      if (arrivesAt) connectionsCovered.add(arrivesAt.connectionId);
      const starts = startedBy(kind.id) ?? startedBy(system.id) ?? startedAt(arrivesAt);
      add('messages', {
        id: `message:${system.id}:${kind.id}`, kind: 'message', who: system.name, what: kind.name,
        standsForId: kind.id, ...(starts ? { starts } : {}), today: arrivesAt?.serviceName ?? '', offersOrchestrator: false,
      });
    }

  const notified = notifying.filter(({ predicate }) => !leftOut.has(predicate.Id));
  const notifiedConnections = new Set(notified.map(({ connectionId }) => connectionId));
  const predicatesSeen = new Set<string>();
  for (const { predicate, connectionId } of notified) {
    if (predicatesSeen.has(predicate.Id)) continue;
    predicatesSeen.add(predicate.Id);
    const called = byId.get(connectionId);
    const starts = startedBy(predicate.Id) ?? byId.get(predicate.Id)?.startsPipeline ?? (called && startedAt(called));
    add('relationships', {
      id: `relationship:${predicate.Id}`, kind: 'relationship', who: '', what: predicate.Name,
      standsForId: predicate.Id, ...(starts ? { starts } : {}), today: called?.serviceName ?? '', offersOrchestrator: false,
    });
  }

  const seenRanges = new Set<string>();
  for (const connection of connections) {
    if (connection.trigger === 'http') {
      if (connectionsCovered.has(connection.connectionId)) continue;
      const starts = startedAt(connection);
      add('messages', {
        id: `httpConnection:${connection.connectionId}`, kind: 'message', who: '', what: connection.subdomain || connection.name,
        standsForId: connection.connectionId, ...(starts ? { starts } : {}), today: connection.serviceName, offersOrchestrator: false,
      });
    } else if (connection.trigger === 'graph') {
      if (predicatesSeen.has(connection.connectionId) || notifiedConnections.has(connection.connectionId)) continue;
      const starts = startedBy(connection.connectionId) ?? connection.startsPipeline;
      add('relationships', {
        id: `relationship:${connection.connectionId}`, kind: 'relationship', who: '', what: connection.name,
        standsForId: connection.connectionId, ...(starts ? { starts } : {}), today: '', namesNoConnectionAlong: notifiesName, offersOrchestrator: false,
      });
    } else if (connection.watches) {
      // A state several connections watch is one row: drawn if any of them draws.
      const { rangeId, name, kindJudged, everySeconds } = connection.watches;
      if (seenRanges.has(rangeId)) continue;
      seenRanges.add(rangeId);
      const watching = connections.filter((each) => each.watches?.rangeId === rangeId);
      const starts = startedBy(rangeId) ?? watching.find((each) => each.startsPipeline)?.startsPipeline;
      // A state several connections watch has no single binding to hand over, so it is offered nothing.
      const handling: StateHandling | undefined = watching.length === 1 ? {
        connectionId: watching[0].connectionId,
        ...(watching[0].bindingId ? { bindingId: watching[0].bindingId } : {}),
        byOrchestrator: orchestratorId !== undefined && watching[0].serviceId === orchestratorId,
      } : undefined;
      add('states', {
        id: `state:${rangeId}`, kind: 'state', who: kindJudged, what: name,
        standsForId: rangeId, ...(starts ? { starts } : {}), today: watching.find((each) => each.serviceName)?.serviceName ?? '',
        ...(everySeconds > 0 ? { everySeconds } : {}),
        ...(handling ? { handling } : {}),
        offersOrchestrator: orchestratorId !== undefined && handling !== undefined && !handling.byOrchestrator,
      });
    }
  }

  for (const cadence of model.cadences()) {
    const starts = startedBy(cadence.id);
    add('clock', {
      id: `cadence:${cadence.id}`, kind: 'cadence', who: '', what: cadence.name, standsForId: cadence.id,
      ...(starts ? { starts } : {}), today: '', ...(cadence.everyMinutes > 0 ? { everyMinutes: cadence.everyMinutes } : {}),
      offersOrchestrator: false,
    });
  }

  return (['messages', 'states', 'clock', 'relationships'] as const).map((name) => ({ name, rows: ordered(rows[name]) }));
}

type OutputKind = 'answer' | 'pipeline' | 'externalSystem' | 'stop';

export interface OutputRow {
  id: string;
  kind: OutputKind;
  name: string;
  /** For an external system: the kinds of message it is told. */
  told?: string[];
}

/** What a run may leave behind: the answer to whatever started it, another pipeline started by a follow-on node,
 *  a system told by a tell node, and the runs a stop node stops. Each node is offered only where the model binds its
 *  kind, a system only where it names the connection it is told through, and a stop node only where a request can
 *  name the run that wrote it, since Phloem refuses each of those otherwise. The open pipeline is not offered to itself. */
export function outputRail(model: PipelineModel, openedPipelineId: string | null): OutputRow[] {
  const pipelines = model.boundTo(ROLE.PipelineFollowOn)
    ? model.thingsOf(ROLE.Pipeline)
      .filter((pipeline) => pipeline.Id !== openedPipelineId)
      .map((pipeline) => ({ id: pipeline.Id, kind: 'pipeline' as const, name: pipeline.Name }))
      .sort((a, b) => a.name.localeCompare(b.name))
    : [];
  const systems = model.boundTo(ROLE.PipelineTell)
    ? model.externalSystems()
      .filter((system) => system.toldThroughConnectionId)
      .map((system) => ({ id: system.id, kind: 'externalSystem' as const, name: system.name, told: system.told.map((kind) => kind.name) }))
    : [];
  const stop = model.boundTo(ROLE.PipelineStop) && model.boundTo(ROLE.RequestedBy) ? [{ id: 'stop', kind: 'stop' as const, name: '' }] : [];
  return [{ id: 'answer', kind: 'answer', name: '' }, ...pipelines, ...systems, ...stop];
}
