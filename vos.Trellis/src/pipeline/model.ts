import type { VosThing, VosRelationship } from '../types/vos';

// Client-side mirror of Phloem's graph reads (vos.Service.Phloem): resolve a node's dispatch
// PlatformServiceConnection, its ports (via the bound service's is-chain), and identify wires by the PipelineWire archetype.
// `is`/`has` are the built-in predicates (matched by name); wires are matched by archetype, never by "feeds".

export const ARCHETYPE = {
  Pipeline: 'Pipeline',
  PipelineNode: 'PipelineNode',
  // Boundary nodes (#5873): a pipeline's external input ("from the start") and output ("at the end").
  PipelineInput: 'PipelineInput',
  PipelineOutput: 'PipelineOutput',
  Connection: 'PlatformServiceConnection',
  Service: 'Service',
  Port: 'Port',
  PipelineWire: 'PipelineWire',
  PipelineRun: 'PipelineRun',
  NodeRun: 'NodeRun',
} as const;

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
  private readonly isOfTypeCache = new Map<string, boolean>();

  constructor(things: VosThing[], rels: VosRelationship[]) {
    this.things = things;
    this.byId = new Map(things.map((t) => [t.Id, t]));
    this.bySubject = new Map();
    for (const rel of rels) {
      const arr = this.bySubject.get(rel.SubjectId);
      if (arr) arr.push(rel);
      else this.bySubject.set(rel.SubjectId, [rel]);
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

  /** Transitive `is`-type membership (memoised — pure function of the graph). */
  isOfType(thingId: string, archetype: string): boolean {
    const key = `${thingId} ${archetype.toLowerCase()}`;
    const cached = this.isOfTypeCache.get(key);
    if (cached !== undefined) return cached;
    const result = this.isOfTypeRec(thingId, archetype.toLowerCase(), new Set());
    this.isOfTypeCache.set(key, result);
    return result;
  }

  private isOfTypeRec(thingId: string, archetypeLower: string, seen: Set<string>): boolean {
    if (seen.has(thingId)) return false;
    seen.add(thingId);
    const t = this.byId.get(thingId);
    if (!t) return false;
    if (t.Name.toLowerCase() === archetypeLower) return true;
    return this.outgoing(thingId, 'is').some((p) => this.isOfTypeRec(p.Id, archetypeLower, seen));
  }

  /** Collect a service's ports by walking its `is`-chain and gathering `has` → Port at each level. */
  resolvePorts(serviceId: string): PortInfo[] {
    const ports: PortInfo[] = [];
    const seen = new Set<string>();
    const stack = [serviceId];
    while (stack.length) {
      const id = stack.pop()!;
      if (seen.has(id)) continue;
      seen.add(id);
      for (const t of this.outgoing(id, 'has')) {
        if (this.isOfType(t.Id, ARCHETYPE.Port)) ports.push(this.toPort(t));
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

  /** Is this thing a boundary node (Input or Output archetype)? (#5873) */
  boundaryKind(thingId: string): 'input' | 'output' | undefined {
    if (this.isOfType(thingId, ARCHETYPE.PipelineInput)) return 'input';
    if (this.isOfType(thingId, ARCHETYPE.PipelineOutput)) return 'output';
    return undefined;
  }

  /** A boundary node's own declared Port child-Things, each with its Thing id — the save-diff needs the id
   * to update a port in place or retract a removed one (#5873). Ports are declared directly on the node. */
  boundaryPortRels(nodeId: string): { portId: string; port: PortInfo }[] {
    return this.outgoing(nodeId, 'has')
      .filter((t) => this.isOfType(t.Id, ARCHETYPE.Port))
      .map((t) => ({ portId: t.Id, port: this.toPort(t) }));
  }

  /** Every dispatchable Connection (has a Subdomain + a bound Service) — the editor palette. */
  connections(): ConnectionInfo[] {
    const result: ConnectionInfo[] = [];
    for (const t of this.things) {
      if (!this.isOfType(t.Id, ARCHETYPE.Connection)) continue;
      const subdomain = t.Properties.Subdomain;
      if (typeof subdomain !== 'string' || subdomain.length === 0) continue;
      const service = this.outgoing(t.Id, 'has').find((s) => this.isOfType(s.Id, ARCHETYPE.Service));
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

  /** Outgoing wires from a node: edges whose predicate is a PipelineWire, with their port mapping and the
   * optional field-paths (#5874). */
  outgoingWires(subjectId: string): { targetId: string; fromPort: string; toPort: string; fromPath: string; toPath: string; transform: string }[] {
    const rels = this.bySubject.get(subjectId);
    if (!rels) return [];
    const out: { targetId: string; fromPort: string; toPort: string; fromPath: string; toPath: string; transform: string }[] = [];
    for (const rel of rels) {
      if (this.isOfType(rel.PredicateId, ARCHETYPE.PipelineWire))
        out.push({
          targetId: rel.TargetId,
          fromPort: String(rel.Properties.fromPort ?? ''),
          toPort: String(rel.Properties.toPort ?? ''),
          fromPath: String(rel.Properties.fromPath ?? ''),
          toPath: String(rel.Properties.toPath ?? ''),
          transform: String(rel.Properties.transform ?? ''),
        });
    }
    return out;
  }

  /** Outgoing wires with their relationship Id — the save diff needs the id to delete a removed wire. */
  outgoingWireRels(subjectId: string): { relId: string; targetId: string; fromPort: string; toPort: string; fromPath: string; toPath: string; transform: string }[] {
    const rels = this.bySubject.get(subjectId);
    if (!rels) return [];
    const out: { relId: string; targetId: string; fromPort: string; toPort: string; fromPath: string; toPath: string; transform: string }[] = [];
    for (const rel of rels) {
      if (this.isOfType(rel.PredicateId, ARCHETYPE.PipelineWire))
        out.push({
          relId: rel.Id,
          targetId: rel.TargetId,
          fromPort: String(rel.Properties.fromPort ?? ''),
          toPort: String(rel.Properties.toPort ?? ''),
          fromPath: String(rel.Properties.fromPath ?? ''),
          toPath: String(rel.Properties.toPath ?? ''),
          transform: String(rel.Properties.transform ?? ''),
        });
    }
    return out;
  }

  /** Id of a predicate Thing matched by name (only ever the built-in is/has on the write path). */
  predicateIdByName(name: string): string | undefined {
    return this.things.find((t) => t.Name.toLowerCase() === name.toLowerCase())?.Id;
  }

  /** Id of the wire predicate — the predicate Thing that is a PipelineWire (never matched by "feeds"). */
  wirePredicateId(): string | undefined {
    return this.things.find((t) => this.isOfType(t.Id, ARCHETYPE.PipelineWire) && t.Name.toLowerCase() !== ARCHETYPE.PipelineWire.toLowerCase())?.Id;
  }

  /** Id of an archetype Thing (the `is` target), e.g. Pipeline / PipelineNode. */
  archetypeId(archetype: string): string | undefined {
    return this.things.find((t) => t.Name.toLowerCase() === archetype.toLowerCase())?.Id;
  }

  /** Live per-node status for a run, keyed by the node Thing id — the SSE animation source (#5635).
   * Phloem records each node's progress on a NodeRun (run -has-> NodeRun) carrying `nodeId` + `status`. Only the
   * node's AGGREGATE NodeRun (no `index`) drives the ring; per-item fan-out NodeRuns are counted separately. */
  nodeRunStatuses(runId: string): Record<string, string> {
    const out: Record<string, string> = {};
    for (const nr of this.outgoing(runId, 'has')) {
      if (nr.Properties.index !== undefined) continue; // per-item fan-out NodeRun — see nodeRunProgress
      const nodeId = nr.Properties.nodeId;
      if (typeof nodeId === 'string' && nodeId) out[nodeId] = String(nr.Properties.status ?? '');
    }
    return out;
  }

  /** Fan-out progress per node (#5648): from the per-item NodeRuns (those carrying an `index`), how many have
   * reached a terminal status out of the total. Empty for non-fan-out nodes. */
  nodeRunProgress(runId: string): Record<string, { done: number; total: number }> {
    const out: Record<string, { done: number; total: number }> = {};
    for (const nr of this.outgoing(runId, 'has')) {
      if (nr.Properties.index === undefined) continue; // aggregate NodeRun
      const nodeId = nr.Properties.nodeId;
      if (typeof nodeId !== 'string' || !nodeId) continue;
      const entry = out[nodeId] ?? { done: 0, total: 0 };
      entry.total = Math.max(entry.total, Number(nr.Properties.total ?? 0));
      if (String(nr.Properties.status ?? '') !== 'running') entry.done += 1;
      out[nodeId] = entry;
    }
    return out;
  }

  /** A run's overall status (running/succeeded/failed/cancelled), if the PipelineRun is in the model yet. */
  runStatus(runId: string): string | undefined {
    const s = this.byId.get(runId)?.Properties.status;
    return typeof s === 'string' ? s : undefined;
  }

  /** Past + in-flight runs of a pipeline (PipelineRun -of-> pipeline), newest first — the history panel (#5646). */
  runsOf(pipelineId: string): RunInfo[] {
    const runs: RunInfo[] = [];
    for (const t of this.things) {
      if (!this.isOfType(t.Id, ARCHETYPE.PipelineRun)) continue;
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
