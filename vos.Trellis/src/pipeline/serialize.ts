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
}

export interface EditorEdge {
  id: string;
  source: string;
  sourceHandle: string;
  target: string;
  targetHandle: string;
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
  const pipelineArch = model.archetypeId(ARCHETYPE.Pipeline);
  const nodeArch = model.archetypeId(ARCHETYPE.PipelineNode);
  if (!isId || !hasId || !wireId || !pipelineArch || !nodeArch)
    throw new Error('Model is missing pipeline archetypes/predicates — load the pipeline seed first.');

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
    { Name: 'is', Subject: pipelineId, Predicate: isId, Target: pipelineArch },
  ];
  for (const n of nodes) {
    const tid = nodeThingId.get(n.id)!;
    const props: Record<string, unknown> = { x: env(DOUBLE, n.x), y: env(DOUBLE, n.y) };
    if (n.paramBindings && Object.keys(n.paramBindings).length > 0)
      props.paramBindings = env(STRING, JSON.stringify(n.paramBindings));
    things.push({ Id: tid, Name: n.label, Properties: props });
    relationships.push({ Name: 'is', Subject: tid, Predicate: isId, Target: nodeArch });
    relationships.push({ Name: 'has', Subject: tid, Predicate: hasId, Target: n.connectionId });
    relationships.push({ Name: 'has', Subject: pipelineId, Predicate: hasId, Target: tid });
  }
  await modelApi.applyFragment(JSON.stringify({ Name: name, Things: things, Relationships: relationships }));

  // Wires: diff desired against persisted so edits add/remove without duplication.
  const wireKey = (from: string, fp: string, to: string, tp: string) => `${from}|${fp}|${to}|${tp}`;
  const desired = edges
    .map((e) => ({ from: nodeThingId.get(e.source), fp: e.sourceHandle, to: nodeThingId.get(e.target), tp: e.targetHandle }))
    .filter((w): w is { from: string; fp: string; to: string; tp: string } => Boolean(w.from && w.to));
  const desiredKeys = new Set(desired.map((w) => wireKey(w.from, w.fp, w.to, w.tp)));

  const persistedNodeIds = existingPipelineId
    ? model.outgoing(existingPipelineId, 'has').filter((t) => model.isOfType(t.Id, ARCHETYPE.PipelineNode)).map((t) => t.Id)
    : [];
  const persistedWires = persistedNodeIds.flatMap((nid) =>
    model.outgoingWireRels(nid).map((w) => ({ relId: w.relId, key: wireKey(nid, w.fromPort, w.targetId, w.toPort) })));
  const persistedKeys = new Set(persistedWires.map((w) => w.key));

  for (const w of desired) {
    if (persistedKeys.has(wireKey(w.from, w.fp, w.to, w.tp))) continue;
    const rel = await relationshipApi.create(w.from, wireId, w.to);
    await relationshipApi.setProperty(rel.Id, 'fromPort', STRING, w.fp);
    await relationshipApi.setProperty(rel.Id, 'toPort', STRING, w.tp);
  }
  for (const w of persistedWires) if (!desiredKeys.has(w.key)) await relationshipApi.remove(w.relId);

  // Deleted nodes: persisted PipelineNodes no longer on the canvas are retracted.
  const desiredThingIds = new Set(nodeThingId.values());
  for (const nid of persistedNodeIds) if (!desiredThingIds.has(nid)) await thingApi.remove(nid);

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
    const conn = model.outgoing(t.Id, 'has').find((c) => model.isOfType(c.Id, ARCHETYPE.Connection));
    return {
      id: t.Id,
      connectionId: conn?.Id ?? '',
      label: t.Name,
      x: Number(t.Properties.x ?? i * 280),
      y: Number(t.Properties.y ?? 80),
      ports: conn ? connectionsById.get(conn.Id)?.ports ?? [] : [],
      paramBindings: parseParamBindings(t.Properties.paramBindings),
    };
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
      });
    }

  return { name: pipe.Name, nodes, edges };
}
