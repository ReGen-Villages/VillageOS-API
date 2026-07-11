import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { modelApi } from '../api/modelApi';
import { PipelineModel, ARCHETYPE, type PortInfo } from './model';

// Persist / read a pipeline as Things + relationships (the lean-on-model bet): the editor is just CRUD over
// thingApi / relationshipApi. Save shape mirrors the seed — node -has-> Connection, wires by the PipelineWire
// predicate carrying fromPort/toPort. Node canvas position round-trips as x/y properties on the node Thing.

const DOUBLE = 'vos.Double';
const STRING = 'vos.String';
const env = (typeInfo: string, value: unknown) => ({ typeInfo, value });

export interface EditorNode {
  id: string;
  connectionId: string;
  label: string;
  x: number;
  y: number;
  ports: PortInfo[];
  /** Input-port name → run-param key (#5647). Persisted as a JSON `paramBindings` property on the node. */
  paramBindings?: Record<string, string>;
  /** Boundary node (#5873): 'input' (a param source) or 'output' (the run's result sink). A boundary node
   * binds no Connection — its `ports` are user-declared and persisted as its own Port child-Things. */
  kind?: 'input' | 'output';
}

export interface EditorEdge {
  id: string;
  source: string;
  sourceHandle: string;
  target: string;
  targetHandle: string;
  /** Field-level mapping (#5874): extract this dotted from-path of the upstream output and place it at this
   * dotted to-path of the downstream input. Empty = the whole payload. */
  fromPath?: string;
  toPath?: string;
}

export interface LoadedPipeline {
  name: string;
  nodes: EditorNode[];
  edges: EditorEdge[];
}

/** The result of a save: the new Pipeline id and the canvas-node-id → Thing-id map so the editor can map
 * live NodeRun statuses (keyed by Thing id) back onto its canvas nodes for animation (#5635). */
export interface SavedPipeline {
  pipelineId: string;
  nodeIdMap: Record<string, string>;
}

// Save the editor state to the model. With no existingPipelineId this creates a new pipeline; with one
// it updates that pipeline IN PLACE — existing node Things keep their Ids (no duplicate pipeline/nodes),
// removed nodes and wires are retracted. The Thing graph (pipeline + nodes + is/has edges) rides ONE
// idempotent fragment upsert (#5808); wires are written per-entity because /api/model/fragment does not
// carry a relationship's fromPort/toPort properties.
export async function savePipeline(
  name: string,
  nodes: EditorNode[],
  edges: EditorEdge[],
  model: PipelineModel,
  existingPipelineId?: string,
): Promise<SavedPipeline> {
  const isId = model.predicateIdByName('is');
  const hasId = model.predicateIdByName('has');
  const wireId = model.wirePredicateId();
  const pipelineArchetype = model.archetypeId(ARCHETYPE.Pipeline);
  const nodeArchetype = model.archetypeId(ARCHETYPE.PipelineNode);
  if (!isId || !hasId || !wireId || !pipelineArchetype || !nodeArchetype)
    throw new Error('Model is missing pipeline archetypes/predicates — load the pipeline seed first.');

  const portArchetype = model.archetypeId(ARCHETYPE.Port);
  const boundaryArchetype = (kind: 'input' | 'output') =>
    model.archetypeId(kind === 'input' ? ARCHETYPE.PipelineInput : ARCHETYPE.PipelineOutput);
  if (nodes.some((n) => n.kind) && (!portArchetype || !boundaryArchetype('input') || !boundaryArchetype('output')))
    throw new Error('Model is missing boundary-node archetypes (PipelineInput/PipelineOutput/Port) — load a seed that defines them.');

  const pipelineId = existingPipelineId ?? crypto.randomUUID();

  // A loaded node's canvas id IS its Thing id (see loadPipeline); a new node ("nX") gets a fresh id so
  // the whole Thing graph can ride one id-keyed fragment upsert (create + update in place, no dupes).
  const nodeThingId = new Map<string, string>();
  for (const n of nodes)
    nodeThingId.set(n.id, model.isOfType(n.id, ARCHETYPE.PipelineNode) ? n.id : crypto.randomUUID());

  const things: Array<{ Id: string; Name: string; Properties: Record<string, unknown> }> = [
    { Id: pipelineId, Name: name, Properties: {} },
  ];
  const relationships: Array<{ Name: string; Subject: string; Predicate: string; Target: string }> = [
    { Name: 'is', Subject: pipelineId, Predicate: isId, Target: pipelineArchetype },
  ];
  // A boundary node's persisted ports, matched by name to reuse ids on update (retract removed ones below).
  const portsToRetract: string[] = [];
  for (const n of nodes) {
    const tid = nodeThingId.get(n.id)!;
    const props: Record<string, unknown> = { x: env(DOUBLE, n.x), y: env(DOUBLE, n.y) };
    if (n.paramBindings && Object.keys(n.paramBindings).length > 0)
      props.paramBindings = env(STRING, JSON.stringify(n.paramBindings));
    things.push({ Id: tid, Name: n.label, Properties: props });
    // Boundary nodes are PipelineNodes too (so the run + editor pick them up), plus their own Input/Output
    // archetype which marks them a param source / result sink for Phloem.
    relationships.push({ Name: 'is', Subject: tid, Predicate: isId, Target: nodeArchetype });
    relationships.push({ Name: 'has', Subject: pipelineId, Predicate: hasId, Target: tid });

    if (n.kind) {
      relationships.push({ Name: 'is', Subject: tid, Predicate: isId, Target: boundaryArchetype(n.kind)! });
      // Declared ports become Port child-Things (has → Port). Reuse a persisted port's id when the name
      // matches (idempotent update); a persisted port no longer declared is retracted.
      const persisted = new Map(model.boundaryPortRels(n.id).map((r) => [r.port.portName, r.portId]));
      for (const p of n.ports) {
        const portId = persisted.get(p.portName) ?? crypto.randomUUID();
        persisted.delete(p.portName);
        things.push({
          Id: portId,
          Name: p.portName,
          Properties: {
            portName: env(STRING, p.portName),
            direction: env(STRING, p.direction),
            type: env(STRING, p.type || 'any'),
            required: env(STRING, String(p.required)),
          },
        });
        relationships.push({ Name: 'is', Subject: portId, Predicate: isId, Target: portArchetype! });
        relationships.push({ Name: 'has', Subject: tid, Predicate: hasId, Target: portId });
      }
      for (const staleId of persisted.values()) portsToRetract.push(staleId);
    } else {
      relationships.push({ Name: 'has', Subject: tid, Predicate: hasId, Target: n.connectionId });
    }
  }
  await modelApi.applyFragment(JSON.stringify({ Name: name, Things: things, Relationships: relationships }));

  // Wires: diff desired against persisted so edits add/remove without duplication.
  const wireKey = (from: string, fp: string, to: string, tp: string) => `${from}|${fp}|${to}|${tp}`;
  // A wire is identified by its endpoints + ports; the field-paths are editable properties on that same wire.
  const desired = edges
    .map((e) => ({ from: nodeThingId.get(e.source), fp: e.sourceHandle, to: nodeThingId.get(e.target), tp: e.targetHandle, fromPath: e.fromPath ?? '', toPath: e.toPath ?? '' }))
    .filter((w): w is { from: string; fp: string; to: string; tp: string; fromPath: string; toPath: string } => Boolean(w.from && w.to));
  const desiredKeys = new Set(desired.map((w) => wireKey(w.from, w.fp, w.to, w.tp)));

  const persistedNodeIds = existingPipelineId
    ? model.outgoing(existingPipelineId, 'has').filter((t) => model.isOfType(t.Id, ARCHETYPE.PipelineNode)).map((t) => t.Id)
    : [];
  const persistedWires = persistedNodeIds.flatMap((nid) =>
    model.outgoingWireRels(nid).map((w) => ({ relId: w.relId, key: wireKey(nid, w.fromPort, w.targetId, w.toPort), fromPath: w.fromPath, toPath: w.toPath })));
  const persistedByKey = new Map(persistedWires.map((w) => [w.key, w]));

  for (const w of desired) {
    const existing = persistedByKey.get(wireKey(w.from, w.fp, w.to, w.tp));
    if (existing) {
      // Wire persists — only re-write a field-path that actually changed.
      if (w.fromPath !== existing.fromPath) await relationshipApi.setProperty(existing.relId, 'fromPath', STRING, w.fromPath);
      if (w.toPath !== existing.toPath) await relationshipApi.setProperty(existing.relId, 'toPath', STRING, w.toPath);
      continue;
    }
    const rel = await relationshipApi.create(w.from, wireId, w.to);
    await relationshipApi.setProperty(rel.Id, 'fromPort', STRING, w.fp);
    await relationshipApi.setProperty(rel.Id, 'toPort', STRING, w.tp);
    if (w.fromPath) await relationshipApi.setProperty(rel.Id, 'fromPath', STRING, w.fromPath);
    if (w.toPath) await relationshipApi.setProperty(rel.Id, 'toPath', STRING, w.toPath);
  }
  for (const w of persistedWires) if (!desiredKeys.has(w.key)) await relationshipApi.remove(w.relId);

  // Ports removed from a boundary node (still on the canvas) are retracted.
  for (const portId of portsToRetract) await thingApi.remove(portId);

  // Deleted nodes: persisted PipelineNodes no longer on the canvas are retracted, along with any Port
  // child-Things a removed boundary node declared (so they don't linger as orphans).
  const desiredThingIds = new Set(nodeThingId.values());
  for (const nid of persistedNodeIds)
    if (!desiredThingIds.has(nid)) {
      for (const r of model.boundaryPortRels(nid)) await thingApi.remove(r.portId);
      await thingApi.remove(nid);
    }

  return { pipelineId, nodeIdMap: Object.fromEntries(nodeThingId) };
}

/** Parse a node's persisted `paramBindings` JSON property (input-port → run-param key); tolerant of junk. */
function parseParamBindings(raw: unknown): Record<string, string> | undefined {
  if (typeof raw !== 'string' || raw.trim() === '') return undefined;
  try {
    const parsed = JSON.parse(raw);
    if (!parsed || typeof parsed !== 'object') return undefined;
    const out: Record<string, string> = {};
    for (const [port, key] of Object.entries(parsed)) if (typeof key === 'string' && key) out[port] = key;
    return Object.keys(out).length > 0 ? out : undefined;
  } catch {
    return undefined;
  }
}

/** Reconstruct the editor state for an existing pipeline from the loaded model (pure read). */
export function loadPipeline(pipelineId: string, model: PipelineModel): LoadedPipeline | null {
  const pipe = model.thing(pipelineId);
  if (!pipe || !model.isOfType(pipelineId, ARCHETYPE.Pipeline)) return null;

  const connectionsById = new Map(model.connections().map((c) => [c.connectionId, c]));
  const nodeThings = model.outgoing(pipelineId, 'has').filter((t) => model.isOfType(t.Id, ARCHETYPE.PipelineNode));
  const nodeIds = new Set(nodeThings.map((t) => t.Id));

  const nodes: EditorNode[] = nodeThings.map((t, i) => {
    const base = {
      id: t.Id,
      label: t.Name,
      x: Number(t.Properties.x ?? i * 280),
      y: Number(t.Properties.y ?? 80),
      paramBindings: parseParamBindings(t.Properties.paramBindings),
    };
    // Boundary node (#5873): its ports are declared on the node itself, and it binds no Connection.
    const kind = model.boundaryKind(t.Id);
    if (kind) return { ...base, kind, connectionId: '', ports: model.boundaryPortRels(t.Id).map((r) => r.port) };

    const conn = model.outgoing(t.Id, 'has').find((c) => model.isOfType(c.Id, ARCHETYPE.Connection));
    return { ...base, connectionId: conn?.Id ?? '', ports: conn ? connectionsById.get(conn.Id)?.ports ?? [] : [] };
  });

  const edges: EditorEdge[] = [];
  for (const t of nodeThings)
    for (const wire of model.outgoingWires(t.Id)) {
      if (!nodeIds.has(wire.targetId)) continue;
      edges.push({
        id: `${t.Id}:${wire.fromPort}->${wire.targetId}:${wire.toPort}`,
        source: t.Id,
        sourceHandle: wire.fromPort,
        target: wire.targetId,
        targetHandle: wire.toPort,
        fromPath: wire.fromPath || undefined,
        toPath: wire.toPath || undefined,
      });
    }

  return { name: pipe.Name, nodes, edges };
}
