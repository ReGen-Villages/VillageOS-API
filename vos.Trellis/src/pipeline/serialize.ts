import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { modelApi } from '../api/modelApi';
import { PipelineModel, ARCHETYPE_FLAG, type PortInfo } from './model';
import type { VosTypeName } from '../utils/constants';

// Persist / read a pipeline as Things + relationships (the lean-on-model bet): the editor is just CRUD over
// thingApi / relationshipApi. Save shape mirrors the seed — node -has-> connection, and a wire is an edge
// through the predicate the model marks as holding wires, carrying fromPort/toPort. Every archetype an `is`
// edge is written to is the one the model marks with that role, never one this file names. Node canvas
// position round-trips as x/y properties on the node Thing.

const DOUBLE: VosTypeName = 'vos.Double';
const STRING: VosTypeName = 'vos.String';
const env = (typeInfo: VosTypeName, value: unknown) => ({ typeInfo, value });

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
   * binds no connection — its `ports` are user-declared and persisted as its own port child-Things. */
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
  /** On-wire JSONata transform (#5875): reshape the extracted value before it is placed at the to-path. */
  transform?: string;
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
  const pipelineArchetype = model.archetypeCarrying(ARCHETYPE_FLAG.Pipeline);
  const nodeArchetype = model.archetypeCarrying(ARCHETYPE_FLAG.PipelineNode);
  // A wire is written as a Thing of the wire archetype, pointing at its target through the wire predicate,
  // so a save needs both — the archetype to say what the Thing is, the predicate to say where it goes.
  const wireArchetype = model.archetypeCarrying(ARCHETYPE_FLAG.PipelineWire);
  if (!isId || !hasId || !wireId || !wireArchetype || !pipelineArchetype || !nodeArchetype)
    throw new Error('This model marks no archetype as a pipeline, a pipeline node or a wire — load a seed that marks them.');

  const portArchetype = model.archetypeCarrying(ARCHETYPE_FLAG.Port);
  const boundaryArchetype = (kind: 'input' | 'output') =>
    model.archetypeCarrying(kind === 'input' ? ARCHETYPE_FLAG.PipelineInput : ARCHETYPE_FLAG.PipelineOutput);
  if (nodes.some((n) => n.kind) && (!portArchetype || !boundaryArchetype('input') || !boundaryArchetype('output')))
    throw new Error('This model marks no archetype as a port, a pipeline input or a pipeline output — load a seed that marks them.');

  const pipelineId = existingPipelineId ?? crypto.randomUUID();

  // A loaded node's canvas id IS its Thing id (see loadPipeline); a new node ("nX") gets a fresh id so
  // the whole Thing graph can ride one id-keyed fragment upsert (create + update in place, no dupes).
  const nodeThingId = new Map<string, string>();
  for (const n of nodes)
    nodeThingId.set(n.id, model.isOfArchetypeCarrying(n.id, ARCHETYPE_FLAG.PipelineNode) ? n.id : crypto.randomUUID());

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
    // Boundary nodes are pipeline nodes too (so the run + editor pick them up), plus the archetype marked
    // as the pipeline's input or its output, which is what makes one a param source or a result sink.
    relationships.push({ Name: 'is', Subject: tid, Predicate: isId, Target: nodeArchetype });
    relationships.push({ Name: 'has', Subject: pipelineId, Predicate: hasId, Target: tid });

    if (n.kind) {
      relationships.push({ Name: 'is', Subject: tid, Predicate: isId, Target: boundaryArchetype(n.kind)! });
      // Declared ports become child-Things under the archetype marked as holding ports. Reuse a persisted
      // port's id when the name matches (idempotent update); a port no longer declared is retracted.
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
    .map((e) => ({ from: nodeThingId.get(e.source), fp: e.sourceHandle, to: nodeThingId.get(e.target), tp: e.targetHandle, fromPath: e.fromPath ?? '', toPath: e.toPath ?? '', transform: e.transform ?? '' }))
    .filter((w): w is { from: string; fp: string; to: string; tp: string; fromPath: string; toPath: string; transform: string } => Boolean(w.from && w.to));
  const desiredKeys = new Set(desired.map((w) => wireKey(w.from, w.fp, w.to, w.tp)));

  const persistedNodeIds = existingPipelineId
    ? model.outgoing(existingPipelineId, 'has').filter((t) => model.isOfArchetypeCarrying(t.Id, ARCHETYPE_FLAG.PipelineNode)).map((t) => t.Id)
    : [];
  const persistedWires = persistedNodeIds.flatMap((nid) =>
    model.outgoingWires(nid).map((w) => ({ ...w, key: wireKey(nid, w.fromPort, w.targetId, w.toPort) })));
  const persistedByKey = new Map(persistedWires.map((w) => [w.key, w]));

  // A wire persisted in either shape is edited through its own shape's calls: a relationship for one drawn
  // as an edge, a Thing for one held. A wire that is new is written held, which is the only shape that lets
  // a node pair carry more than one.
  const writeOn = (shape: 'edge' | 'held') =>
    shape === 'edge' ? relationshipApi.setProperty : thingApi.setProperty;

  for (const w of desired) {
    const existing = persistedByKey.get(wireKey(w.from, w.fp, w.to, w.tp));
    if (existing) {
      // Wire persists — only re-write a field-path or transform that actually changed.
      const write = writeOn(existing.shape);
      if (w.fromPath !== existing.fromPath) await write(existing.wireId, 'fromPath', STRING, w.fromPath);
      if (w.toPath !== existing.toPath) await write(existing.wireId, 'toPath', STRING, w.toPath);
      if (w.transform !== existing.transform) await write(existing.wireId, 'transform', STRING, w.transform);
      continue;
    }
    const wire = await thingApi.create(`${w.fp} to ${w.tp}`);
    // Every value is declared at creation, empty where unset, because a Thing's property write updates and
    // does not create — so a path added later is an update rather than a call that answers "no such property".
    await thingApi.addProperty(wire.Id, 'fromPort', STRING, w.fp);
    await thingApi.addProperty(wire.Id, 'toPort', STRING, w.tp);
    await thingApi.addProperty(wire.Id, 'fromPath', STRING, w.fromPath);
    await thingApi.addProperty(wire.Id, 'toPath', STRING, w.toPath);
    await thingApi.addProperty(wire.Id, 'transform', STRING, w.transform);
    await relationshipApi.create(wire.Id, isId, wireArchetype);
    await relationshipApi.create(w.from, hasId, wire.Id);
    await relationshipApi.create(wire.Id, wireId, w.to);
  }
  for (const w of persistedWires)
    if (!desiredKeys.has(w.key))
      await (w.shape === 'edge' ? relationshipApi.remove(w.wireId) : thingApi.remove(w.wireId));

  // Ports removed from a boundary node (still on the canvas) are retracted.
  for (const portId of portsToRetract) await thingApi.remove(portId);

  // Deleted nodes: persisted pipeline nodes no longer on the canvas are retracted, along with any port
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
  if (!pipe || !model.isOfArchetypeCarrying(pipelineId, ARCHETYPE_FLAG.Pipeline)) return null;

  const connectionsById = new Map(model.connections().map((c) => [c.connectionId, c]));
  const nodeThings = model.outgoing(pipelineId, 'has').filter((t) => model.isOfArchetypeCarrying(t.Id, ARCHETYPE_FLAG.PipelineNode));
  const nodeIds = new Set(nodeThings.map((t) => t.Id));

  const nodes: EditorNode[] = nodeThings.map((t, i) => {
    const base = {
      id: t.Id,
      label: t.Name,
      x: Number(t.Properties.x ?? i * 280),
      y: Number(t.Properties.y ?? 80),
      paramBindings: parseParamBindings(t.Properties.paramBindings),
    };
    // Boundary node (#5873): its ports are declared on the node itself, and it binds no connection.
    const kind = model.boundaryKind(t.Id);
    if (kind) return { ...base, kind, connectionId: '', ports: model.boundaryPortRels(t.Id).map((r) => r.port) };

    const conn = model.outgoing(t.Id, 'has').find((c) => model.isOfArchetypeCarrying(c.Id, ARCHETYPE_FLAG.Connection));
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
        transform: wire.transform || undefined,
      });
    }

  return { name: pipe.Name, nodes, edges };
}
