import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { PipelineModel, ARCHETYPE, type PortInfo } from './model';

// Persist / read a pipeline as Things + relationships (the lean-on-model bet): the editor is just CRUD over
// thingApi / relationshipApi. Save shape mirrors the seed — node -has-> Connection, wires by the PipelineWire
// predicate carrying fromPort/toPort. Node canvas position round-trips as x/y properties on the node Thing.

export interface EditorNode {
  id: string;
  connectionId: string;
  label: string;
  x: number;
  y: number;
  ports: PortInfo[];
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

export async function savePipeline(
  name: string,
  nodes: EditorNode[],
  edges: EditorEdge[],
  model: PipelineModel,
): Promise<string> {
  const isId = model.predicateIdByName('is');
  const hasId = model.predicateIdByName('has');
  const wireId = model.wirePredicateId();
  const pipelineArch = model.archetypeId(ARCHETYPE.Pipeline);
  const nodeArch = model.archetypeId(ARCHETYPE.PipelineNode);
  if (!isId || !hasId || !wireId || !pipelineArch || !nodeArch)
    throw new Error('Model is missing pipeline archetypes/predicates — load the pipeline seed first.');

  const pipeline = await thingApi.create(name);
  await relationshipApi.create(pipeline.Id, isId, pipelineArch);

  const nodeThingId = new Map<string, string>();
  for (const n of nodes) {
    const thing = await thingApi.create(n.label);
    await thingApi.setProperty(thing.Id, 'x', 'vos.Double', n.x);
    await thingApi.setProperty(thing.Id, 'y', 'vos.Double', n.y);
    await relationshipApi.create(thing.Id, isId, nodeArch);
    await relationshipApi.create(thing.Id, hasId, n.connectionId);
    await relationshipApi.create(pipeline.Id, hasId, thing.Id);
    nodeThingId.set(n.id, thing.Id);
  }

  for (const e of edges) {
    const from = nodeThingId.get(e.source);
    const to = nodeThingId.get(e.target);
    if (!from || !to) continue;
    const rel = await relationshipApi.create(from, wireId, to);
    await relationshipApi.setProperty(rel.Id, 'fromPort', 'vos.String', e.sourceHandle);
    await relationshipApi.setProperty(rel.Id, 'toPort', 'vos.String', e.targetHandle);
  }

  return pipeline.Id;
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
