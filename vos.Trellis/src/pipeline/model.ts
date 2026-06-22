import type { VosThing, VosRelationship } from '../types/vos';

// Client-side mirror of Phloem's graph reads (vos.ManagedMicroservice.Phloem): resolve a node's dispatch
// Connection, its ports (via the bound service's is-chain), and identify wires by the PipelineWire archetype.
// `is`/`has` are the built-in predicates (matched by name); wires are matched by archetype, never by "feeds".

export const ARCHETYPE = {
  Pipeline: 'Pipeline',
  PipelineNode: 'PipelineNode',
  Connection: 'Connection',
  Service: 'Service',
  Port: 'Port',
  PipelineWire: 'PipelineWire',
} as const;

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
  private readonly rels: VosRelationship[];
  private readonly byId: Map<string, VosThing>;
  constructor(things: VosThing[], rels: VosRelationship[]) {
    this.things = things;
    this.rels = rels;
    this.byId = new Map(things.map((t) => [t.Id, t]));
  }

  thing(id: string): VosThing | undefined {
    return this.byId.get(id);
  }

  private predicateName(rel: VosRelationship): string | undefined {
    return this.byId.get(rel.PredicateId)?.Name;
  }

  /** Targets reached from `subjectId` via a predicate matched by name (the built-in is/has). */
  outgoing(subjectId: string, predicateName: string): VosThing[] {
    const out: VosThing[] = [];
    for (const rel of this.rels) {
      if (rel.SubjectId !== subjectId) continue;
      if (this.predicateName(rel)?.toLowerCase() === predicateName.toLowerCase()) {
        const t = this.byId.get(rel.TargetId);
        if (t) out.push(t);
      }
    }
    return out;
  }

  /** Transitive `is`-type membership. */
  isOfType(thingId: string, archetype: string, seen = new Set<string>()): boolean {
    if (seen.has(thingId)) return false;
    seen.add(thingId);
    const t = this.byId.get(thingId);
    if (!t) return false;
    if (t.Name.toLowerCase() === archetype.toLowerCase()) return true;
    return this.outgoing(thingId, 'is').some((p) => this.isOfType(p.Id, archetype, seen));
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

  /** Outgoing wires from a node: edges whose predicate is a PipelineWire, with their port mapping. */
  outgoingWires(subjectId: string): { targetId: string; fromPort: string; toPort: string }[] {
    const out: { targetId: string; fromPort: string; toPort: string }[] = [];
    for (const rel of this.rels) {
      if (rel.SubjectId !== subjectId) continue;
      if (this.isOfType(rel.PredicateId, ARCHETYPE.PipelineWire))
        out.push({
          targetId: rel.TargetId,
          fromPort: String(rel.Properties.fromPort ?? ''),
          toPort: String(rel.Properties.toPort ?? ''),
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
}

/** Are two ports type-compatible? Empty/"any" on either side is a wildcard (mirrors DagValidator). */
export function typesCompatible(outType: string, inType: string): boolean {
  const a = outType.trim().toLowerCase();
  const b = inType.trim().toLowerCase();
  return a === '' || b === '' || a === 'any' || b === 'any' || a === b;
}
