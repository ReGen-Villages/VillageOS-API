import type { VosThing, VosRelationship } from '../types/vos';

// Client-side mirror of Phloem's graph reads (vos.Service.Phloem): resolve a node's dispatch connection, its
// ports (via the bound service's is-chain), and identify wires by the archetype their predicate is of.
// `is`, `has` and `of` are the platform's own built-in predicates and are matched by name. Which Thing plays
// which role is not a name at all: it is read from the flag the archetype carries, the same contract Phloem
// reads, so a model may call every archetype below whatever suits it.

export const ARCHETYPE_FLAG = {
  Pipeline: '__IsPipelineArchetype',
  PipelineNode: '__IsPipelineNodeArchetype',
  // Boundary nodes: a pipeline's external input ("from the start") and output ("at the end").
  PipelineInput: '__IsPipelineInputArchetype',
  PipelineOutput: '__IsPipelineOutputArchetype',
  Connection: '__IsConnectionArchetype',
  Service: '__IsServiceArchetype',
  Port: '__IsPortArchetype',
  PipelineWire: '__IsPipelineWireArchetype',
  PipelineRun: '__IsPipelineRunArchetype',
  Range: '__IsRangeArchetype',
  // A system outside the platform that sends it messages or is sent them, and a kind of message crossing
  // that boundary. Declared by the model like every other role; nothing here names either archetype.
  ExternalSystem: '__IsExternalSystemArchetype',
  MessageKind: '__IsMessageKindArchetype',
} as const;

/** The marks on the predicates the page walks. The platform declares the first three and dispatches on
 *  them; the rest say what an external system sends and is told, where a kind of message arrives, what a
 *  boundary node stands for, and which pipeline a connection's trigger starts. */
export const PREDICATE_FLAG = {
  Trigger: '__IsTriggerPredicate',
  StateWatch: '__IsStateWatchPredicate',
  JudgedThing: '__IsJudgedThingPredicate',
  Sends: '__IsSendsPredicate',
  Told: '__IsToldPredicate',
  ArrivesAt: '__IsArrivesAtPredicate',
  StandsFor: '__IsStandsForPredicate',
  PipelineStart: '__IsPipelineStartPredicate',
} as const;

const EVALUATION_INTERVAL_PROPERTY = 'EvaluationIntervalSeconds';

/** What a boundary node may stand for, told by what the Thing it points at is. */
export type EndKind = 'messageKind' | 'externalSystem' | 'door' | 'state' | 'relationship' | 'pipeline' | 'other';

/** A Thing at one end of a pipeline: what it is, and whether a run may come from it or leave it behind. */
export interface EndInformation {
  id: string;
  name: string;
  kind: EndKind;
  mayStart: boolean;
  mayEnd: boolean;
}

export interface PipelineNamed {
  id: string;
  name: string;
}

/** A kind of message an external system sends or is told, and the door it arrives at where one is declared. */
export interface MessageKindInformation {
  id: string;
  name: string;
  arrivesAtConnectionId?: string;
}

export interface ExternalSystemInformation {
  id: string;
  name: string;
  sends: MessageKindInformation[];
  told: MessageKindInformation[];
}

/** A connection as a catalyst: how it is reached, what it dispatches, and what it starts. */
export interface CatalystConnection {
  connectionId: string;
  name: string;
  /** The trigger's own name — the platform's are `http`, `graph` and `state`. Empty where none is declared. */
  trigger: string;
  subdomain: string;
  serviceName: string;
  /** The range a state connection watches: the state itself, the kind it judges, and how often the clock re-checks it. */
  watches?: { rangeId: string; name: string; kindJudged: string; everySeconds: number };
  startsPipeline?: PipelineNamed;
}

/** A Thing plays a role when it holds that flag as its own property, set true. Inheritance hands an
 *  archetype's flag down to every member, so this reads own properties and never the inherited view.
 *  A flag written as anything but a boolean is not a mark: Phloem reads it the same strict way, and a
 *  model the editor accepted but the orchestrator refused would be the split this replaced. */
function carriesFlag(thing: VosThing, roleFlag: string): boolean {
  return thing.Properties[roleFlag] === true;
}

export interface RunInformation {
  runId: string;
  status: string;
  startedUtc: string;
}

export interface PortInformation {
  portName: string;
  direction: 'in' | 'out';
  type: string;
  required: boolean;
}

/** A wire as the editor reads it, whichever shape the model holds it in. */
export interface WireRead {
  wireId: string;
  shape: 'edge' | 'held';
  targetId: string;
  fromPort: string;
  toPort: string;
  fromPath: string;
  toPath: string;
  transform: string;
}

/** The five values a wire carries, read the same off a relationship and off a wire Thing. */
function wireMapping(properties: Record<string, unknown>) {
  return {
    fromPort: String(properties.fromPort ?? ''),
    toPort: String(properties.toPort ?? ''),
    fromPath: String(properties.fromPath ?? ''),
    toPath: String(properties.toPath ?? ''),
    transform: String(properties.transform ?? ''),
  };
}

export interface ConnectionInformation {
  connectionId: string;
  name: string;
  subdomain: string;
  serviceId: string;
  ports: PortInformation[];
}

export class PipelineModel {
  private readonly things: VosThing[];
  private readonly byId: Map<string, VosThing>;
  // Relationships indexed by subject, so a traversal costs a node's own relationships rather than every relationship in the model.
  private readonly bySubject: Map<string, VosRelationship[]>;
  // The Things this model uses as a predicate. A wire held as a Thing is of the wire archetype exactly as
  // the wire predicate is, so being of that archetype no longer tells the two apart — being used as a
  // predicate does.
  private readonly usedAsPredicate: Set<string>;
  private readonly roleCache = new Map<string, boolean>();
  // Relationships indexed by target, for the reads that ask what points at a Thing: which node stands for
  // a catalyst, which door a kind arrives at. Built once with the subject index.
  private readonly byTarget: Map<string, VosRelationship[]>;

  constructor(things: VosThing[], relationships: VosRelationship[]) {
    this.things = things;
    this.byId = new Map(things.map((t) => [t.Id, t]));
    this.bySubject = new Map();
    this.byTarget = new Map();
    this.usedAsPredicate = new Set();
    for (const relationship of relationships) {
      const arr = this.bySubject.get(relationship.SubjectId);
      if (arr) arr.push(relationship);
      else this.bySubject.set(relationship.SubjectId, [relationship]);
      const incoming = this.byTarget.get(relationship.TargetId);
      if (incoming) incoming.push(relationship);
      else this.byTarget.set(relationship.TargetId, [relationship]);
      this.usedAsPredicate.add(relationship.PredicateId);
    }
  }

  /** The predicate Thing carrying a mark — the one a relationship in that role is written through.
   *  Undefined where the model marks none, which is a relationship this editor cannot write. */
  predicateCarrying(predicateFlag: string): string | undefined {
    return this.things.find((t) => carriesFlag(t, predicateFlag))?.Id;
  }

  /** Targets reached from `subjectId` along any predicate carrying the mark. */
  reachedAlong(subjectId: string, predicateFlag: string): VosThing[] {
    const out: VosThing[] = [];
    for (const relationship of this.bySubject.get(subjectId) ?? []) {
      const predicate = this.byId.get(relationship.PredicateId);
      if (!predicate || !carriesFlag(predicate, predicateFlag)) continue;
      const target = this.byId.get(relationship.TargetId);
      if (target) out.push(target);
    }
    return out;
  }

  /** Subjects that reach `targetId` along any predicate carrying the mark. */
  private reachingAlong(targetId: string, predicateFlag: string): VosThing[] {
    const out: VosThing[] = [];
    for (const relationship of this.byTarget.get(targetId) ?? []) {
      const predicate = this.byId.get(relationship.PredicateId);
      if (!predicate || !carriesFlag(predicate, predicateFlag)) continue;
      const subject = this.byId.get(relationship.SubjectId);
      if (subject) out.push(subject);
    }
    return out;
  }

  /** Every Thing that is of an archetype carrying the mark — the members, never the archetype itself. */
  thingsOfArchetypeCarrying(roleFlag: string): VosThing[] {
    return this.things.filter((t) => !t.IsArchetype && this.isOfArchetypeCarrying(t.Id, roleFlag));
  }

  /** The name of the trigger a connection is reached by: its own, or the nearest declared above it along
   *  `is`, which is how the platform resolves it. Empty where nothing declares one. */
  triggerOf(connectionId: string): string {
    const seen = new Set<string>();
    const queue = [connectionId];
    while (queue.length) {
      const id = queue.shift()!;
      if (seen.has(id)) continue;
      seen.add(id);
      const trigger = this.reachedAlong(id, PREDICATE_FLAG.Trigger)[0];
      if (trigger) return trigger.Name.trim().toLowerCase();
      for (const parent of this.outgoing(id, 'is')) queue.push(parent.Id);
    }
    return '';
  }

  private serviceBoundTo(connectionId: string): VosThing | undefined {
    return this.outgoing(connectionId, 'has').find((s) => this.isOfArchetypeCarrying(s.Id, ARCHETYPE_FLAG.Service));
  }

  /** Every connection the model holds, as the catalyst its trigger makes it, whether or not a pipeline
   *  can dispatch it: a door reached over HTTP, a predicate written along, a state watched. */
  catalystConnections(): CatalystConnection[] {
    return this.thingsOfArchetypeCarrying(ARCHETYPE_FLAG.Connection).map((connection) => {
      const watched = this.reachedAlong(connection.Id, PREDICATE_FLAG.StateWatch)
        .find((range) => this.isOfArchetypeCarrying(range.Id, ARCHETYPE_FLAG.Range));
      const started = this.reachedAlong(connection.Id, PREDICATE_FLAG.PipelineStart)
        .find((pipeline) => this.isOfArchetypeCarrying(pipeline.Id, ARCHETYPE_FLAG.Pipeline));
      const subdomain = connection.Properties.Subdomain;
      const interval = watched ? Number(watched.Properties[EVALUATION_INTERVAL_PROPERTY] ?? 0) : 0;
      return {
        connectionId: connection.Id,
        name: connection.Name,
        trigger: this.triggerOf(connection.Id),
        subdomain: typeof subdomain === 'string' ? subdomain : '',
        serviceName: this.serviceBoundTo(connection.Id)?.Name ?? '',
        ...(watched ? {
          watches: {
            rangeId: watched.Id,
            name: watched.Name,
            kindJudged: this.reachedAlong(watched.Id, PREDICATE_FLAG.JudgedThing)[0]?.Name ?? '',
            everySeconds: Number.isFinite(interval) && interval > 0 ? interval : 0,
          },
        } : {}),
        ...(started ? { startsPipeline: { id: started.Id, name: started.Name } } : {}),
      };
    });
  }

  private messageKind(kind: VosThing): MessageKindInformation {
    const door = this.reachedAlong(kind.Id, PREDICATE_FLAG.ArrivesAt)
      .find((c) => this.isOfArchetypeCarrying(c.Id, ARCHETYPE_FLAG.Connection));
    return { id: kind.Id, name: kind.Name, ...(door ? { arrivesAtConnectionId: door.Id } : {}) };
  }

  /** Every external system the model holds, with the kinds of message it sends and is told. */
  externalSystems(): ExternalSystemInformation[] {
    const kinds = (systemId: string, predicateFlag: string) =>
      this.reachedAlong(systemId, predicateFlag)
        .filter((kind) => this.isOfArchetypeCarrying(kind.Id, ARCHETYPE_FLAG.MessageKind))
        .map((kind) => this.messageKind(kind))
        .sort((a, b) => a.name.localeCompare(b.name));
    return this.thingsOfArchetypeCarrying(ARCHETYPE_FLAG.ExternalSystem)
      .map((system) => ({
        id: system.Id,
        name: system.Name,
        sends: kinds(system.Id, PREDICATE_FLAG.Sends),
        told: kinds(system.Id, PREDICATE_FLAG.Told),
      }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  /** What a boundary node stands for, and the relationship that says so — the save replaces it through
   *  that relationship's id. Undefined for a node standing for nothing, which at the end of a run is the
   *  answer and at the start is a run started by hand. */
  standsFor(nodeId: string): { thingId: string; relationshipId: string } | undefined {
    for (const relationship of this.bySubject.get(nodeId) ?? []) {
      const predicate = this.byId.get(relationship.PredicateId);
      if (predicate && carriesFlag(predicate, PREDICATE_FLAG.StandsFor) && this.byId.has(relationship.TargetId))
        return { thingId: relationship.TargetId, relationshipId: relationship.Id };
    }
    return undefined;
  }

  /** The pipeline drawn for a catalyst or an outcome: the one whose boundary node stands for the Thing,
   *  at the given end. */
  pipelineWhoseEndStandsFor(thingId: string, end: 'input' | 'output'): PipelineNamed | undefined {
    const endFlag = end === 'input' ? ARCHETYPE_FLAG.PipelineInput : ARCHETYPE_FLAG.PipelineOutput;
    for (const node of this.reachingAlong(thingId, PREDICATE_FLAG.StandsFor)) {
      if (!this.isOfArchetypeCarrying(node.Id, endFlag)) continue;
      const pipeline = (this.byTarget.get(node.Id) ?? [])
        .map((relationship) => this.byId.get(relationship.SubjectId))
        .find((subject) => subject && this.isOfArchetypeCarrying(subject.Id, ARCHETYPE_FLAG.Pipeline));
      if (pipeline) return { id: pipeline.Id, name: pipeline.Name };
    }
    return undefined;
  }

  /** What a Thing is as the end of a pipeline, and which end it may be. A kind of message may start a run
   *  where a system sends it and end one where a system is told it; a system may start one where it sends
   *  anything, and may always be told; a door, a state and a predicate only start; a pipeline does both. */
  endInformation(thingId: string): EndInformation {
    const thing = this.byId.get(thingId);
    const named = { id: thingId, name: thing?.Name ?? '' };
    if (!thing) return { ...named, kind: 'other', mayStart: false, mayEnd: false };
    if (this.isOfArchetypeCarrying(thingId, ARCHETYPE_FLAG.MessageKind)) {
      const sent = this.reachingAlong(thingId, PREDICATE_FLAG.Sends).length > 0;
      const told = this.reachingAlong(thingId, PREDICATE_FLAG.Told).length > 0;
      return { ...named, kind: 'messageKind', mayStart: sent, mayEnd: told };
    }
    if (this.isOfArchetypeCarrying(thingId, ARCHETYPE_FLAG.ExternalSystem))
      return { ...named, kind: 'externalSystem', mayStart: this.reachedAlong(thingId, PREDICATE_FLAG.Sends).length > 0, mayEnd: true };
    if (this.isOfArchetypeCarrying(thingId, ARCHETYPE_FLAG.Pipeline))
      return { ...named, kind: 'pipeline', mayStart: true, mayEnd: true };
    if (this.isOfArchetypeCarrying(thingId, ARCHETYPE_FLAG.Range))
      return { ...named, kind: 'state', mayStart: true, mayEnd: false };
    if (this.isOfArchetypeCarrying(thingId, ARCHETYPE_FLAG.Connection)) {
      const trigger = this.triggerOf(thingId);
      const kind: EndKind = trigger === 'graph' ? 'relationship' : trigger === 'state' ? 'state' : 'door';
      return { ...named, kind, mayStart: true, mayEnd: false };
    }
    return { ...named, kind: 'other', mayStart: false, mayEnd: false };
  }

  thing(id: string): VosThing | undefined {
    return this.byId.get(id);
  }

  /** Targets reached from `subjectId` via a predicate matched by name (the built-in is/has). */
  outgoing(subjectId: string, predicateName: string): VosThing[] {
    const relationships = this.bySubject.get(subjectId);
    if (!relationships) return [];
    const pn = predicateName.toLowerCase();
    const out: VosThing[] = [];
    for (const relationship of relationships) {
      if (this.byId.get(relationship.PredicateId)?.Name.toLowerCase() === pn) {
        const t = this.byId.get(relationship.TargetId);
        if (t) out.push(t);
      }
    }
    return out;
  }

  /** Is this Thing — directly or through its `is`-chain — of an archetype carrying the given role? The walk
   *  starts above the Thing, so an archetype never plays its own role (memoised: pure over the graph). */
  isOfArchetypeCarrying(thingId: string, roleFlag: string): boolean {
    const key = `${thingId} ${roleFlag}`;
    const cached = this.roleCache.get(key);
    if (cached !== undefined) return cached;
    const result = this.isOfArchetypeCarryingRec(thingId, roleFlag, new Set([thingId]));
    this.roleCache.set(key, result);
    return result;
  }

  private isOfArchetypeCarryingRec(thingId: string, roleFlag: string, seen: Set<string>): boolean {
    for (const parent of this.outgoing(thingId, 'is')) {
      if (seen.has(parent.Id)) continue;
      seen.add(parent.Id);
      if (carriesFlag(parent, roleFlag)) return true;
      if (this.isOfArchetypeCarryingRec(parent.Id, roleFlag, seen)) return true;
    }
    return false;
  }

  resolvePorts(serviceId: string): PortInformation[] {
    const ports: PortInformation[] = [];
    const seen = new Set<string>();
    const stack = [serviceId];
    while (stack.length) {
      const id = stack.pop()!;
      if (seen.has(id)) continue;
      seen.add(id);
      for (const t of this.outgoing(id, 'has')) {
        if (this.isOfArchetypeCarrying(t.Id, ARCHETYPE_FLAG.Port)) ports.push(this.toPort(t));
      }
      for (const parent of this.outgoing(id, 'is')) stack.push(parent.Id);
    }
    return ports;
  }

  private toPort(t: VosThing): PortInformation {
    const p = t.Properties;
    return {
      portName: String(p.portName ?? t.Name),
      direction: String(p.direction ?? 'in').toLowerCase() === 'out' ? 'out' : 'in',
      type: String(p.type ?? ''),
      required: String(p.required ?? 'false').toLowerCase() === 'true',
    };
  }

  /** Is this thing a boundary node — of the archetype marked as a pipeline's input, or as its output? */
  boundaryKind(thingId: string): 'input' | 'output' | undefined {
    if (this.isOfArchetypeCarrying(thingId, ARCHETYPE_FLAG.PipelineInput)) return 'input';
    if (this.isOfArchetypeCarrying(thingId, ARCHETYPE_FLAG.PipelineOutput)) return 'output';
    return undefined;
  }

  /** A boundary node's own declared port child-Things, each with its Thing id — the save-diff needs the id
   * to update a port in place or retract a removed one. Ports are declared directly on the node. */
  boundaryPortRelationships(nodeId: string): { portId: string; port: PortInformation }[] {
    return this.outgoing(nodeId, 'has')
      .filter((t) => this.isOfArchetypeCarrying(t.Id, ARCHETYPE_FLAG.Port))
      .map((t) => ({ portId: t.Id, port: this.toPort(t) }));
  }

  /** Every dispatchable connection (carries a Subdomain and binds a service) — the editor palette. */
  connections(): ConnectionInformation[] {
    const result: ConnectionInformation[] = [];
    for (const t of this.things) {
      if (!this.isOfArchetypeCarrying(t.Id, ARCHETYPE_FLAG.Connection)) continue;
      const subdomain = t.Properties.Subdomain;
      if (typeof subdomain !== 'string' || subdomain.length === 0) continue;
      const service = this.outgoing(t.Id, 'has').find((s) => this.isOfArchetypeCarrying(s.Id, ARCHETYPE_FLAG.Service));
      if (!service) continue;
      result.push({
        connectionId: t.Id,
        name: t.Name,
        subdomain,
        serviceId: service.Id,
        ports: this.resolvePorts(service.Id),
      });
    }
    return result;
  }

  /** Outgoing wires from a node, in either shape the model may hold them in, with their port mapping and
   * the optional field-paths. `wireId` is what the save edits and removes the wire through, and
   * `shape` says which call that is: a relationship for a wire drawn as a relationship, a Thing for one held. */
  outgoingWires(subjectId: string): WireRead[] {
    const relationships = this.bySubject.get(subjectId);
    if (!relationships) return [];
    const out: WireRead[] = [];
    for (const relationship of relationships) {
      // Drawn as a relationship: the predicate is of the wire archetype and the relationship carries the mapping.
      if (this.isOfArchetypeCarrying(relationship.PredicateId, ARCHETYPE_FLAG.PipelineWire)) {
        out.push({ wireId: relationship.Id, shape: 'edge', targetId: relationship.TargetId, ...wireMapping(relationship.Properties) });
        continue;
      }
      // Held as a Thing: the node `has` a Thing of the wire archetype, and that Thing points at the node
      // the wire carries into. A wire pointing at nothing that is a node is half-drawn and is skipped.
      const held = this.byId.get(relationship.TargetId);
      if (!held || !this.isOfArchetypeCarrying(held.Id, ARCHETYPE_FLAG.PipelineWire)) continue;
      const target = (this.bySubject.get(held.Id) ?? [])
        .map((r) => r.TargetId)
        .find((id) => this.isOfArchetypeCarrying(id, ARCHETYPE_FLAG.PipelineNode));
      if (!target) continue;
      out.push({ wireId: held.Id, shape: 'held', targetId: target, ...wireMapping(held.Properties) });
    }
    return out;
  }

  /** Id of a predicate Thing matched by name (only ever the built-in is/has on the write path). */
  predicateIdByName(name: string): string | undefined {
    return this.things.find((t) => t.Name.toLowerCase() === name.toLowerCase())?.Id;
  }

  /** Id of the wire predicate — the predicate Thing of the archetype the model marks as holding wires.
   *  A wire held as a Thing is of that same archetype, so this also asks that the Thing is one the model
   *  uses as a predicate; without that it returns whichever the snapshot happens to list first, and a save
   *  writes every new wire through a Thing that is not a predicate at all. */
  wirePredicateId(): string | undefined {
    return this.things.find(
      (t) => this.usedAsPredicate.has(t.Id) && this.isOfArchetypeCarrying(t.Id, ARCHETYPE_FLAG.PipelineWire),
    )?.Id;
  }

  /** Id of the archetype this model marks with the given role — the Thing an `is` relationship is written to.
   *  Undefined when the model marks the role on nothing, which is a model this editor cannot author.
   *  Seed validation refuses a model that marks one role on two archetypes, so the first is the only one. */
  archetypeCarrying(roleFlag: string): string | undefined {
    return this.things.find((t) => carriesFlag(t, roleFlag))?.Id;
  }

  /** Live per-node status for a run, keyed by the node Thing id — the SSE animation source.
   * Phloem records each node's progress on a node-run Thing the run `has`, carrying `nodeId` + `status`. Only the
   * node's AGGREGATE record (no `index`) drives the ring; per-item fan-out records are counted separately. */
  nodeRunStatuses(runId: string): Record<string, string> {
    const out: Record<string, string> = {};
    for (const nodeRun of this.outgoing(runId, 'has')) {
      if (nodeRun.Properties.index !== undefined) continue; // per-item fan-out record — see nodeRunProgress
      const nodeId = nodeRun.Properties.nodeId;
      if (typeof nodeId === 'string' && nodeId) out[nodeId] = String(nodeRun.Properties.status ?? '');
    }
    return out;
  }

  /** Fan-out progress per node: from the per-item records (those carrying an `index`), how many have
   * reached a terminal status out of the total. Empty for non-fan-out nodes. */
  nodeRunProgress(runId: string): Record<string, { done: number; total: number }> {
    const out: Record<string, { done: number; total: number }> = {};
    for (const nodeRun of this.outgoing(runId, 'has')) {
      if (nodeRun.Properties.index === undefined) continue; // aggregate record
      const nodeId = nodeRun.Properties.nodeId;
      if (typeof nodeId !== 'string' || !nodeId) continue;
      const entry = out[nodeId] ?? { done: 0, total: 0 };
      entry.total = Math.max(entry.total, Number(nodeRun.Properties.total ?? 0));
      if (String(nodeRun.Properties.status ?? '') !== 'running') entry.done += 1;
      out[nodeId] = entry;
    }
    return out;
  }

  runStatus(runId: string): string | undefined {
    const s = this.byId.get(runId)?.Properties.status;
    return typeof s === 'string' ? s : undefined;
  }

  /** Past + in-flight runs of a pipeline (a run Thing `of` the pipeline), newest first — the history panel. */
  runsOf(pipelineId: string): RunInformation[] {
    const runs: RunInformation[] = [];
    for (const t of this.things) {
      if (!this.isOfArchetypeCarrying(t.Id, ARCHETYPE_FLAG.PipelineRun)) continue;
      if (!this.outgoing(t.Id, 'of').some((p) => p.Id === pipelineId)) continue;
      runs.push({ runId: t.Id, status: String(t.Properties.status ?? ''), startedUtc: String(t.Properties.startedUtc ?? '') });
    }
    return runs.sort((a, b) => b.startedUtc.localeCompare(a.startedUtc));
  }
}

/** Are two ports type-compatible? Empty/"any" on either side is a wildcard (mirrors DagValidator). */
export function typesCompatible(outType: string, inType: string): boolean {
  const a = outType.trim().toLowerCase();
  const b = inType.trim().toLowerCase();
  return a === '' || b === '' || a === 'any' || b === 'any' || a === b;
}
