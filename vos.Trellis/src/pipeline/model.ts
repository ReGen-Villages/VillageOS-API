import type { VosThing, VosRelationship } from '../types/vos';

// Client-side mirror of Phloem's graph reads (vos.Service.Phloem): resolve a node's dispatch connection, its
// ports (via the bound service's is-chain), and identify wires by the archetype their predicate is of.
// `is`, `has` and `of` are the platform's own built-in predicates and are matched by name. Which Thing plays
// which role is not a name at all: it is read from the flag the archetype carries, the same contract Phloem
// reads, so a model may call every archetype below whatever suits it.

export const ARCHETYPE_FLAG = {
  Pipeline: '__IsPipelineArchetype',
  PipelineNode: '__IsPipelineNodeArchetype',
  // Boundary nodes (#5873): a pipeline's external input ("from the start") and output ("at the end").
  PipelineInput: '__IsPipelineInputArchetype',
  PipelineOutput: '__IsPipelineOutputArchetype',
  Connection: '__IsConnectionArchetype',
  Service: '__IsServiceArchetype',
  Port: '__IsPortArchetype',
  PipelineWire: '__IsPipelineWireArchetype',
  PipelineRun: '__IsPipelineRunArchetype',
} as const;

/** A Thing plays a role when it holds that flag as its own property, set true. Inheritance hands an
 *  archetype's flag down to every member, so this reads own properties and never the inherited view.
 *  A flag written as anything but a boolean is not a mark: Phloem reads it the same strict way, and a
 *  model the editor accepted but the orchestrator refused would be the split this replaced. */
function carriesFlag(thing: VosThing, roleFlag: string): boolean {
  return thing.Properties[roleFlag] === true;
}

/** A past or in-flight run of a pipeline, for the run-history panel (#5646). */
export interface RunInfo {
  runId: string;
  status: string;
  startedUtc: string;
}

export interface PortInfo {
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

/** The five values a wire carries, read the same off an edge and off a wire Thing. */
function wireMapping(properties: Record<string, unknown>) {
  return {
    fromPort: String(properties.fromPort ?? ''),
    toPort: String(properties.toPort ?? ''),
    fromPath: String(properties.fromPath ?? ''),
    toPath: String(properties.toPath ?? ''),
    transform: String(properties.transform ?? ''),
  };
}

export interface ConnectionInfo {
  connectionId: string;
  name: string;
  subdomain: string;
  serviceId: string;
  ports: PortInfo[];
}

export class PipelineModel {
  private readonly things: VosThing[];
  private readonly byId: Map<string, VosThing>;
  // Relationships indexed by subject so traversal is O(node degree), not O(all relationships).
  private readonly bySubject: Map<string, VosRelationship[]>;
  // The Things this model uses as a predicate. A wire held as a Thing is of the wire archetype exactly as
  // the wire predicate is, so being of that archetype no longer tells the two apart — being used as a
  // predicate does.
  private readonly usedAsPredicate: Set<string>;
  private readonly roleCache = new Map<string, boolean>();

  constructor(things: VosThing[], rels: VosRelationship[]) {
    this.things = things;
    this.byId = new Map(things.map((t) => [t.Id, t]));
    this.bySubject = new Map();
    this.usedAsPredicate = new Set();
    for (const rel of rels) {
      const arr = this.bySubject.get(rel.SubjectId);
      if (arr) arr.push(rel);
      else this.bySubject.set(rel.SubjectId, [rel]);
      this.usedAsPredicate.add(rel.PredicateId);
    }
  }

  thing(id: string): VosThing | undefined {
    return this.byId.get(id);
  }

  /** Targets reached from `subjectId` via a predicate matched by name (the built-in is/has). */
  outgoing(subjectId: string, predicateName: string): VosThing[] {
    const rels = this.bySubject.get(subjectId);
    if (!rels) return [];
    const pn = predicateName.toLowerCase();
    const out: VosThing[] = [];
    for (const rel of rels) {
      if (this.byId.get(rel.PredicateId)?.Name.toLowerCase() === pn) {
        const t = this.byId.get(rel.TargetId);
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

  /** Collect a service's ports by walking its `is`-chain and gathering the port Things it `has` at each level. */
  resolvePorts(serviceId: string): PortInfo[] {
    const ports: PortInfo[] = [];
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

  private toPort(t: VosThing): PortInfo {
    const p = t.Properties;
    return {
      portName: String(p.portName ?? t.Name),
      direction: String(p.direction ?? 'in').toLowerCase() === 'out' ? 'out' : 'in',
      type: String(p.type ?? ''),
      required: String(p.required ?? 'false').toLowerCase() === 'true',
    };
  }

  /** Is this thing a boundary node — of the archetype marked as a pipeline's input, or as its output? (#5873) */
  boundaryKind(thingId: string): 'input' | 'output' | undefined {
    if (this.isOfArchetypeCarrying(thingId, ARCHETYPE_FLAG.PipelineInput)) return 'input';
    if (this.isOfArchetypeCarrying(thingId, ARCHETYPE_FLAG.PipelineOutput)) return 'output';
    return undefined;
  }

  /** A boundary node's own declared port child-Things, each with its Thing id — the save-diff needs the id
   * to update a port in place or retract a removed one (#5873). Ports are declared directly on the node. */
  boundaryPortRels(nodeId: string): { portId: string; port: PortInfo }[] {
    return this.outgoing(nodeId, 'has')
      .filter((t) => this.isOfArchetypeCarrying(t.Id, ARCHETYPE_FLAG.Port))
      .map((t) => ({ portId: t.Id, port: this.toPort(t) }));
  }

  /** Every dispatchable connection (carries a Subdomain and binds a service) — the editor palette. */
  connections(): ConnectionInfo[] {
    const result: ConnectionInfo[] = [];
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
   * the optional field-paths (#5874). `wireId` is what the save edits and removes the wire through, and
   * `shape` says which call that is: a relationship for a wire drawn as an edge, a Thing for one held. */
  outgoingWires(subjectId: string): WireRead[] {
    const rels = this.bySubject.get(subjectId);
    if (!rels) return [];
    const out: WireRead[] = [];
    for (const rel of rels) {
      // Drawn as an edge: the predicate is of the wire archetype and the edge carries the mapping.
      if (this.isOfArchetypeCarrying(rel.PredicateId, ARCHETYPE_FLAG.PipelineWire)) {
        out.push({ wireId: rel.Id, shape: 'edge', targetId: rel.TargetId, ...wireMapping(rel.Properties) });
        continue;
      }
      // Held as a Thing: the node `has` a Thing of the wire archetype, and that Thing points at the node
      // the wire carries into. A wire pointing at nothing that is a node is half-drawn and is skipped.
      const held = this.byId.get(rel.TargetId);
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

  /** Id of the archetype this model marks with the given role — the Thing an `is` edge is written to.
   *  Undefined when the model marks the role on nothing, which is a model this editor cannot author.
   *  Seed validation refuses a model that marks one role on two archetypes, so the first is the only one. */
  archetypeCarrying(roleFlag: string): string | undefined {
    return this.things.find((t) => carriesFlag(t, roleFlag))?.Id;
  }

  /** Live per-node status for a run, keyed by the node Thing id — the SSE animation source (#5635).
   * Phloem records each node's progress on a node-run Thing the run `has`, carrying `nodeId` + `status`. Only the
   * node's AGGREGATE record (no `index`) drives the ring; per-item fan-out records are counted separately. */
  nodeRunStatuses(runId: string): Record<string, string> {
    const out: Record<string, string> = {};
    for (const nr of this.outgoing(runId, 'has')) {
      if (nr.Properties.index !== undefined) continue; // per-item fan-out record — see nodeRunProgress
      const nodeId = nr.Properties.nodeId;
      if (typeof nodeId === 'string' && nodeId) out[nodeId] = String(nr.Properties.status ?? '');
    }
    return out;
  }

  /** Fan-out progress per node (#5648): from the per-item records (those carrying an `index`), how many have
   * reached a terminal status out of the total. Empty for non-fan-out nodes. */
  nodeRunProgress(runId: string): Record<string, { done: number; total: number }> {
    const out: Record<string, { done: number; total: number }> = {};
    for (const nr of this.outgoing(runId, 'has')) {
      if (nr.Properties.index === undefined) continue; // aggregate record
      const nodeId = nr.Properties.nodeId;
      if (typeof nodeId !== 'string' || !nodeId) continue;
      const entry = out[nodeId] ?? { done: 0, total: 0 };
      entry.total = Math.max(entry.total, Number(nr.Properties.total ?? 0));
      if (String(nr.Properties.status ?? '') !== 'running') entry.done += 1;
      out[nodeId] = entry;
    }
    return out;
  }

  /** A run's overall status (running/succeeded/failed/cancelled), if the run Thing is in the model yet. */
  runStatus(runId: string): string | undefined {
    const s = this.byId.get(runId)?.Properties.status;
    return typeof s === 'string' ? s : undefined;
  }

  /** Past + in-flight runs of a pipeline (a run Thing `of` the pipeline), newest first — the history panel (#5646). */
  runsOf(pipelineId: string): RunInfo[] {
    const runs: RunInfo[] = [];
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
