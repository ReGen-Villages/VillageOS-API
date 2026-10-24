import type { VosThing, VosRelationship } from '../types/vos';
import { statedProperties } from '../utils/propertyMapper';

// Client-side mirror of Phloem's graph reads (vos.Service.Phloem): resolve a node's service call connection, its
// ports (via the bound service's is-chain), and identify wires by the archetype their predicate is of.
// `is`, `has`, `of` and `binds` are the platform's own built-in predicates and are matched by name. Which Thing
// plays which role is not a name at all: each role is a Thing the model declares under a fixed identifier, and
// it binds the Thing playing it along `binds` — the same contract Phloem reads, so a model may call every
// archetype and predicate below whatever suits it.

/** Each role's identifier is the standard version 5 identifier of the role's word, so every model declares the
 *  same role Thing. Archetypes first; then the predicates the platform calls services on, what an external system
 *  sends and is told, where a kind of message arrives, what a boundary node stands for, and which pipeline a
 *  connection's trigger starts. */
export const ROLE = {
  Pipeline: 'f8bc9986-6eef-54d9-9940-4aca2497109b',
  PipelineNode: 'f273f058-e3d6-5a20-aa37-34638ce64735',
  // Boundary nodes: a pipeline's external input ("from the start") and output ("at the end").
  PipelineInput: '634f9ae3-2cca-5dd9-84aa-fec4dff07a6d',
  PipelineOutput: '880d2158-25a8-5b2b-8b6a-923f21ca153d',
  Connection: 'd5a3c29f-67c1-55a5-b0e4-9241378bd08d',
  Service: '768bb2e8-e738-5345-9126-c23ad1513197',
  Port: '318d6995-e07b-5c83-acb5-86cd935ba4c6',
  PipelineWire: 'adfe0071-bbaf-5e7f-abc3-255c08d8735a',
  PipelineRun: 'b682b68f-0101-5ab4-a7a9-13c3e43df873',
  Range: 'c081de17-c325-55b7-99ca-16fb835ae545',
  // A system outside the platform that sends it messages or is sent them, and a kind of message crossing
  // that boundary.
  ExternalSystem: '03164f47-30c0-5984-9215-a81317000ec5',
  MessageKind: '0b2082d2-eec5-5eec-85dd-66431d776731',
  Cadence: 'f3883eaf-4e52-5c0c-887b-1bc696578812',
  // The orchestrator's prototype: the rail binds a state's connection to the one service of it.
  Orchestrator: 'da0ee18e-c157-5f94-b2f8-cca2d7337929',
  Trigger: 'd619eb5c-8cca-5234-8826-2f01bd6b4e7b',
  StateWatch: '03590900-eb28-55bf-9731-e9a0c03dce82',
  JudgedThing: '63d59765-a9b7-53b8-8a2b-0de3899add18',
  Sends: 'ab3334f4-cb8e-5513-94b4-43debdb73acd',
  Told: '08104fb0-e74f-511a-9598-62c20da9083c',
  ArrivesAt: '79f0afcb-8c66-53dd-8081-43af167fc07a',
  StandsFor: '4277647a-93f8-5860-8717-da2a12673dbf',
  PipelineStart: '871174ab-c8e4-5712-81e1-b95accaea5ce',
  // The orchestrator's own connection, which a vigil answers through to resume a waiting run; no run starts from it.
  RunResumption: '50c894ed-a33d-56cc-a5a6-92aa82d32f4f',
  // A stop request, and the orchestrator's own connection it is related to a run along; no run starts from it.
  StopRequest: '159ef90f-8089-5055-b5a9-8839d5e28e7a',
  RunStopping: '7acbab6c-58f3-5145-a9c0-18a907197131',
} as const;

const EVALUATION_INTERVAL_PROPERTY = 'EvaluationIntervalSeconds';

/** Written by the orchestrator onto a run it starts from a request; the request log is outside the model,
 *  so the run names the entry rather than relating to it. */
const REQUEST_ID_PROPERTY = 'requestId';

/** What a boundary node may stand for, told by what the Thing it points at is. */
export type EndKind = 'messageKind' | 'externalSystem' | 'door' | 'state' | 'relationship' | 'pipeline' | 'cadence' | 'other';

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

/** A connection as a catalyst: how it is reached, what it calls, and what it starts. */
export interface CatalystConnection {
  connectionId: string;
  name: string;
  /** The trigger's own name — the platform's are `http`, `graph` and `state`. Empty where none is declared. */
  trigger: string;
  subdomain: string;
  serviceName: string;
  /** The `has` relationship binding the connection to its service, and that service. */
  bindingId?: string;
  serviceId?: string;
  /** The range a state connection watches: the state itself, the kind it judges, and how often the clock re-checks it. */
  watches?: { rangeId: string; name: string; kindJudged: string; everySeconds: number };
  startsPipeline?: PipelineNamed;
}

export interface RunInformation {
  runId: string;
  status: string;
  startedUtc: string;
  /** The request-log entry the run started from, where a request started it. */
  requestId?: string;
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
  private readonly boundCache = new Map<string, string | undefined>();
  private readonly statedCache = new WeakMap<VosThing, Record<string, unknown>>();
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

  /** The Thing a role binds: the archetype a Thing of that role `is`, or the predicate a relationship in that
   *  role is written through. Undefined where the model binds the role to nothing, which is a model this editor
   *  cannot author, and where it binds two, which seed validation refuses because a role is one Thing's job. */
  boundTo(roleId: string): string | undefined {
    if (this.boundCache.has(roleId)) return this.boundCache.get(roleId);
    const played = this.outgoing(roleId, 'binds');
    const bound = played.length === 1 ? played[0].Id : undefined;
    this.boundCache.set(roleId, bound);
    return bound;
  }

  /** Targets reached from `subjectId` along the predicate the role binds. */
  reachedAlong(subjectId: string, roleId: string): VosThing[] {
    const predicateId = this.boundTo(roleId);
    const out: VosThing[] = [];
    for (const relationship of this.bySubject.get(subjectId) ?? []) {
      if (relationship.PredicateId !== predicateId) continue;
      const target = this.byId.get(relationship.TargetId);
      if (target) out.push(target);
    }
    return out;
  }

  /** Subjects that reach `targetId` along the predicate the role binds. */
  private reachingAlong(targetId: string, roleId: string): VosThing[] {
    const predicateId = this.boundTo(roleId);
    const out: VosThing[] = [];
    for (const relationship of this.byTarget.get(targetId) ?? []) {
      if (relationship.PredicateId !== predicateId) continue;
      const subject = this.byId.get(relationship.SubjectId);
      if (subject) out.push(subject);
    }
    return out;
  }

  /** Every Thing that `is` what the role binds — the members, never the archetype itself. */
  thingsOf(roleId: string): VosThing[] {
    return this.things.filter((t) => !t.IsArchetype && this.isOf(t.Id, roleId));
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
      const trigger = this.reachedAlong(id, ROLE.Trigger)[0];
      if (trigger) return trigger.Name.trim().toLowerCase();
      for (const parent of this.outgoing(id, 'is')) queue.push(parent.Id);
    }
    return '';
  }

  private bindingOf(connectionId: string): { relationshipId: string; service: VosThing } | undefined {
    for (const relationship of this.bySubject.get(connectionId) ?? []) {
      if (this.byId.get(relationship.PredicateId)?.Name.toLowerCase() !== 'has') continue;
      const service = this.byId.get(relationship.TargetId);
      if (service && this.isOf(service.Id, ROLE.Service))
        return { relationshipId: relationship.Id, service };
    }
    return undefined;
  }

  /** The one service of the orchestrator's archetype. None where the model holds none or several, since
   *  binding a state to one of several would be a guess. */
  orchestratorService(): VosThing | undefined {
    const services = this.thingsOf(ROLE.Orchestrator);
    return services.length === 1 ? services[0] : undefined;
  }

  /** Every connection the model holds, as the catalyst its trigger makes it, whether or not a pipeline
   *  can call it: a door reached over HTTP, a predicate written along, a state watched. */
  catalystConnections(): CatalystConnection[] {
    const resumption = this.boundTo(ROLE.RunResumption);
    return this.thingsOf(ROLE.Connection).filter((connection) => connection.Id !== resumption).map((connection) => {
      const watched = this.reachedAlong(connection.Id, ROLE.StateWatch)
        .find((range) => this.isOf(range.Id, ROLE.Range));
      const started = this.reachedAlong(connection.Id, ROLE.PipelineStart)
        .find((pipeline) => this.isOf(pipeline.Id, ROLE.Pipeline));
      const subdomain = connection.Properties.Subdomain;
      const interval = watched ? Number(watched.Properties[EVALUATION_INTERVAL_PROPERTY] ?? 0) : 0;
      const binding = this.bindingOf(connection.Id);
      return {
        connectionId: connection.Id,
        name: connection.Name,
        trigger: this.triggerOf(connection.Id),
        subdomain: typeof subdomain === 'string' ? subdomain : '',
        serviceName: binding?.service.Name ?? '',
        ...(binding ? { bindingId: binding.relationshipId, serviceId: binding.service.Id } : {}),
        ...(watched ? {
          watches: {
            rangeId: watched.Id,
            name: watched.Name,
            kindJudged: this.reachedAlong(watched.Id, ROLE.JudgedThing)[0]?.Name ?? '',
            everySeconds: Number.isFinite(interval) && interval > 0 ? interval : 0,
          },
        } : {}),
        ...(started ? { startsPipeline: { id: started.Id, name: started.Name } } : {}),
      };
    });
  }

  private messageKind(kind: VosThing): MessageKindInformation {
    const door = this.reachedAlong(kind.Id, ROLE.ArrivesAt)
      .find((c) => this.isOf(c.Id, ROLE.Connection));
    return { id: kind.Id, name: kind.Name, ...(door ? { arrivesAtConnectionId: door.Id } : {}) };
  }

  /** Every external system the model holds, with the kinds of message it sends and is told. */
  externalSystems(): ExternalSystemInformation[] {
    const kinds = (systemId: string, roleId: string) =>
      this.reachedAlong(systemId, roleId)
        .filter((kind) => this.isOf(kind.Id, ROLE.MessageKind))
        .map((kind) => this.messageKind(kind))
        .sort((a, b) => a.name.localeCompare(b.name));
    return this.thingsOf(ROLE.ExternalSystem)
      .map((system) => ({
        id: system.Id,
        name: system.Name,
        sends: kinds(system.Id, ROLE.Sends),
        told: kinds(system.Id, ROLE.Told),
      }))
      .sort((a, b) => a.name.localeCompare(b.name));
  }

  /** What a boundary node stands for, and the relationship that says so — the save replaces it through
   *  that relationship's id. Undefined for a node standing for nothing, which at the end of a run is the
   *  answer and at the start is a run started by hand. */
  standsFor(nodeId: string): { thingId: string; relationshipId: string } | undefined {
    const standsForId = this.boundTo(ROLE.StandsFor);
    for (const relationship of this.bySubject.get(nodeId) ?? []) {
      if (relationship.PredicateId === standsForId && this.byId.has(relationship.TargetId))
        return { thingId: relationship.TargetId, relationshipId: relationship.Id };
    }
    return undefined;
  }

  /** The pipeline drawn for a catalyst or an outcome: the one whose boundary node stands for the Thing,
   *  at the given end. */
  pipelineWhoseEndStandsFor(thingId: string, end: 'input' | 'output'): PipelineNamed | undefined {
    const endRole = end === 'input' ? ROLE.PipelineInput : ROLE.PipelineOutput;
    for (const node of this.reachingAlong(thingId, ROLE.StandsFor)) {
      if (!this.isOf(node.Id, endRole)) continue;
      const pipeline = (this.byTarget.get(node.Id) ?? [])
        .map((relationship) => this.byId.get(relationship.SubjectId))
        .find((subject) => subject && this.isOf(subject.Id, ROLE.Pipeline));
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
    if (this.isOf(thingId, ROLE.MessageKind)) {
      const sent = this.reachingAlong(thingId, ROLE.Sends).length > 0;
      const told = this.reachingAlong(thingId, ROLE.Told).length > 0;
      return { ...named, kind: 'messageKind', mayStart: sent, mayEnd: told };
    }
    if (this.isOf(thingId, ROLE.ExternalSystem))
      return { ...named, kind: 'externalSystem', mayStart: this.reachedAlong(thingId, ROLE.Sends).length > 0, mayEnd: true };
    if (this.isOf(thingId, ROLE.Pipeline))
      return { ...named, kind: 'pipeline', mayStart: true, mayEnd: true };
    if (this.isOf(thingId, ROLE.Range))
      return { ...named, kind: 'state', mayStart: true, mayEnd: false };
    if (this.isOf(thingId, ROLE.Cadence))
      return { ...named, kind: 'cadence', mayStart: true, mayEnd: false };
    if (this.isOf(thingId, ROLE.Connection)) {
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

  /** Does this Thing `is`, directly or through its `is`-chain, what the role binds? The walk starts above the
   *  Thing, so an archetype never plays its own role (memoised: pure over the graph). */
  isOf(thingId: string, roleId: string): boolean {
    const key = `${thingId} ${roleId}`;
    const cached = this.roleCache.get(key);
    if (cached !== undefined) return cached;
    const archetypeId = this.boundTo(roleId);
    const result = archetypeId !== undefined && this.isBeneath(thingId, archetypeId, new Set([thingId]));
    this.roleCache.set(key, result);
    return result;
  }

  private isBeneath(thingId: string, archetypeId: string, seen: Set<string>): boolean {
    for (const parent of this.outgoing(thingId, 'is')) {
      if (seen.has(parent.Id)) continue;
      seen.add(parent.Id);
      if (parent.Id === archetypeId) return true;
      if (this.isBeneath(parent.Id, archetypeId, seen)) return true;
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
        if (this.isOf(t.Id, ROLE.Port)) ports.push(this.toPort(t));
      }
      for (const parent of this.outgoing(id, 'is')) stack.push(parent.Id);
    }
    return ports;
  }

  /** The values a Thing states, own or in its override store, for what the page saved onto a model
   *  whose archetype declares the property. Held per Thing: the model is rebuilt on every store change,
   *  and within one build a port is read for every pipeline that lists it. */
  stated(thingId: string): Record<string, unknown> {
    const thing = this.byId.get(thingId);
    if (!thing) return {};
    let values = this.statedCache.get(thing);
    if (!values) {
      values = statedProperties(thing);
      this.statedCache.set(thing, values);
    }
    return values;
  }

  private toPort(t: VosThing): PortInformation {
    const p = this.stated(t.Id);
    return {
      portName: String(p.portName ?? t.Name),
      direction: String(p.direction ?? 'in').toLowerCase() === 'out' ? 'out' : 'in',
      type: String(p.type ?? ''),
      required: String(p.required ?? 'false').toLowerCase() === 'true',
    };
  }

  /** Is this thing a boundary node — of the archetype bound as a pipeline's input, or as its output? */
  boundaryKind(thingId: string): 'input' | 'output' | undefined {
    if (this.isOf(thingId, ROLE.PipelineInput)) return 'input';
    if (this.isOf(thingId, ROLE.PipelineOutput)) return 'output';
    return undefined;
  }

  /** A boundary node's own declared port child-Things, each with its Thing id — the save-diff needs the id
   * to update a port in place or retract a removed one. Ports are declared directly on the node. */
  boundaryPortRelationships(nodeId: string): { portId: string; port: PortInformation }[] {
    return this.outgoing(nodeId, 'has')
      .filter((t) => this.isOf(t.Id, ROLE.Port))
      .map((t) => ({ portId: t.Id, port: this.toPort(t) }));
  }

  /** Every callable connection (carries a Subdomain and binds a service) — the editor palette. */
  connections(): ConnectionInformation[] {
    const result: ConnectionInformation[] = [];
    for (const t of this.things) {
      if (!this.isOf(t.Id, ROLE.Connection)) continue;
      const subdomain = t.Properties.Subdomain;
      if (typeof subdomain !== 'string' || subdomain.length === 0) continue;
      const service = this.outgoing(t.Id, 'has').find((s) => this.isOf(s.Id, ROLE.Service));
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
      if (this.isOf(relationship.PredicateId, ROLE.PipelineWire)) {
        out.push({ wireId: relationship.Id, shape: 'edge', targetId: relationship.TargetId, ...wireMapping(relationship.Properties) });
        continue;
      }
      // Held as a Thing: the node `has` a Thing of the wire archetype, and that Thing points at the node
      // the wire carries into. A wire pointing at nothing that is a node is half-drawn and is skipped.
      const held = this.byId.get(relationship.TargetId);
      if (!held || !this.isOf(held.Id, ROLE.PipelineWire)) continue;
      const target = (this.bySubject.get(held.Id) ?? [])
        .map((r) => r.TargetId)
        .find((id) => this.isOf(id, ROLE.PipelineNode));
      if (!target) continue;
      out.push({ wireId: held.Id, shape: 'held', targetId: target, ...wireMapping(this.stated(held.Id)) });
    }
    return out;
  }

  /** Id of a predicate Thing matched by name (only ever the built-in is/has on the write path). */
  predicateIdByName(name: string): string | undefined {
    return this.things.find((t) => t.Name.toLowerCase() === name.toLowerCase())?.Id;
  }

  /** Id of the wire predicate — the predicate Thing of the archetype the model binds as holding wires.
   *  A wire held as a Thing is of that same archetype, so being of it does not tell the two apart. The
   *  one the model already uses as a predicate answers first; before any wire is drawn nothing uses it,
   *  so the shape decides: a held wire is always held by a node along `has`, and a predicate never is. */
  wirePredicateId(): string | undefined {
    const ofTheWireArchetype = this.things.filter((t) => this.isOf(t.Id, ROLE.PipelineWire));
    return ofTheWireArchetype.find((t) => this.usedAsPredicate.has(t.Id))?.Id
      ?? ofTheWireArchetype.find((t) => !this.isHeld(t.Id))?.Id;
  }

  private isHeld(thingId: string): boolean {
    return (this.byTarget.get(thingId) ?? []).some((r) => this.byId.get(r.PredicateId)?.Name.toLowerCase() === 'has');
  }

  /** Live per-node status for a run, keyed by the node Thing id — the SSE animation source.
   * Phloem records each node's progress on a node-run Thing the run `has`, carrying `nodeId` + `status`. Only the
   * node's aggregate record (no `index`) drives the ring; per-item fan-out records are counted separately. */
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
      if (!this.isOf(t.Id, ROLE.PipelineRun)) continue;
      if (!this.outgoing(t.Id, 'of').some((p) => p.Id === pipelineId)) continue;
      const requestId = t.Properties[REQUEST_ID_PROPERTY];
      runs.push({
        runId: t.Id,
        status: String(t.Properties.status ?? ''),
        startedUtc: String(t.Properties.startedUtc ?? ''),
        ...(typeof requestId === 'string' && requestId ? { requestId } : {}),
      });
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
