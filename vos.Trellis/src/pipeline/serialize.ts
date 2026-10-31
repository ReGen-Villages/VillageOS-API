import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { modelApi } from '../api/modelApi';
import { PipelineModel, ROLE, type PortInformation } from './model';
import type { VosTypeName } from '../utils/constants';
import i18n from '../i18n';

// Persist / read a pipeline as Things + relationships: the editor only creates, reads, updates and
// deletes through thingApi / relationshipApi. Save shape mirrors the seed — node -has-> connection, and a
// wire is a Thing the node has, carried to its target through the predicate of the wire archetype and related to
// its two ports along the connectors the model binds. Every archetype an `is` relationship is written to is the
// one the model binds to that role, never one this file names. Node canvas position round-trips as x/y properties
// on the node Thing. A canvas handle is its port's Thing identifier, so a wire follows a renamed port.

const DOUBLE: VosTypeName = 'vos.Double';
const STRING: VosTypeName = 'vos.String';
const BOOLEAN: VosTypeName = 'vos.Boolean';
const env = (typeInfo: VosTypeName, value: unknown) => ({ typeInfo, value });

export interface EditorNode {
  id: string;
  connectionId: string;
  label: string;
  x: number;
  y: number;
  ports: PortInformation[];
  /** Input-port name → run-parameter key. Persisted as a JSON `parameterBindings` property on the node. */
  parameterBindings?: Record<string, string>;
  /** Boundary node: 'input' (a param source) or 'output' (the run's result sink). A boundary node
   * binds no connection — its `ports` are user-declared and persisted as its own port child-Things. */
  kind?: 'input' | 'output';
  /** The Thing a boundary node stands for — its catalyst at the start, its outcome at the end — written
   * as a relationship through the predicate the model binds to that role. */
  standsForId?: string;
  choices?: EditorChoice[];
  /** The Output node's rule over what reached it; none means any fail wire reaching it fails the run. */
  rule?: string;
}

export interface EditorChoice {
  id: string;
  name: string;
  when: string;
}

export interface EditorEdge {
  id: string;
  source: string;
  sourceHandle: string;
  target: string;
  targetHandle: string;
  /** Field-level mapping: extract this dotted from-path of the upstream output and place it at this
   * dotted to-path of the downstream input. Empty = the whole payload. */
  fromPath?: string;
  toPath?: string;
  /** On-wire JSONata transform: reshape the extracted value before it is placed at the to-path. */
  transform?: string;
  /** The results and choices the wire carries on: success, fail, or a choice of its source node. */
  takenOn?: string[];
}

/** A wire nobody has marked carries on success, as a newly drawn wire does. */
export function takenOnOf(takenOn: string[] | undefined, model: PipelineModel): string[] {
  const success = model.boundTo(ROLE.Success);
  return takenOn?.length ? takenOn : success ? [success] : [];
}

export interface LoadedPipeline {
  name: string;
  nodes: EditorNode[];
  edges: EditorEdge[];
}

/** What a save needs the model to declare, in the order a refusal names them. */
export const PIPELINE_DECLARATIONS = [
  'isPredicate', 'hasPredicate', 'pipelineArchetype', 'nodeArchetype', 'wireArchetype', 'wirePredicate',
  'inputConnector', 'outputConnector', 'takenOnPredicate', 'successResult',
] as const;
type PipelineDeclaration = (typeof PIPELINE_DECLARATIONS)[number];

/** The result of a save: the new Pipeline id and the canvas-node-id → Thing-id map so the editor can map
 * live NodeRun statuses (keyed by Thing id) back onto its canvas nodes for animation. */
export interface SavedPipeline {
  pipelineId: string;
  nodeIdMap: Record<string, string>;
}

// Save the editor state to the model. With no existingPipelineId this creates a new pipeline; with one
// it updates that pipeline in place — existing node Things keep their Ids (no duplicate pipeline/nodes),
// removed nodes and wires are retracted. The Thing graph (pipeline + nodes + is/has relationships) rides one
// idempotent fragment upsert; wires are written per-entity after it, once every port they relate to exists.
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
  const pipelineArchetype = model.boundTo(ROLE.Pipeline);
  const nodeArchetype = model.boundTo(ROLE.PipelineNode);
  // A wire is written as a Thing of the wire archetype, pointing at its target through the wire predicate,
  // so a save needs both — the archetype to say what the Thing is, the predicate to say where it goes.
  const wireArchetype = model.boundTo(ROLE.PipelineWire);
  const inputConnector = model.boundTo(ROLE.InputConnector);
  const outputConnector = model.boundTo(ROLE.OutputConnector);
  const takenOnId = model.boundTo(ROLE.TakenOn);
  const successId = model.boundTo(ROLE.Success);
  // The refusal names what is missing rather than every declaration at once, which sent the reader after five
  // things when one was absent. These checks are what narrow each to a string below.
  if (!isId || !hasId || !wireId || !wireArchetype || !pipelineArchetype || !nodeArchetype || !inputConnector || !outputConnector
      || !takenOnId || !successId) {
    const found: Record<PipelineDeclaration, string | undefined> = {
      isPredicate: isId, hasPredicate: hasId, pipelineArchetype, nodeArchetype, wireArchetype, wirePredicate: wireId,
      inputConnector, outputConnector, takenOnPredicate: takenOnId, successResult: successId,
    };
    const missing = PIPELINE_DECLARATIONS.filter((declaration) => !found[declaration])
      .map((declaration) => i18n.t(`pipeline.declarations.${declaration}`));
    throw new Error(i18n.t('pipeline.noPipelineDeclarations', { missing: missing.join(', ') }));
  }

  const portArchetype = model.boundTo(ROLE.Port);
  const boundaryArchetype = (kind: 'input' | 'output') =>
    model.boundTo(kind === 'input' ? ROLE.PipelineInput : ROLE.PipelineOutput);
  if (nodes.some((n) => n.kind) && (!portArchetype || !boundaryArchetype('input') || !boundaryArchetype('output')))
    throw new Error(i18n.t('pipeline.noPortRoles'));
  const standsForId = model.boundTo(ROLE.StandsFor);
  if (nodes.some((n) => n.standsForId) && !standsForId)
    throw new Error(i18n.t('pipeline.noStandsForRole'));
  const choiceArchetype = model.boundTo(ROLE.DecisionPath);
  if (nodes.some((n) => n.choices?.length) && !choiceArchetype)
    throw new Error(i18n.t('pipeline.noChoiceRole'));

  const pipelineId = existingPipelineId ?? crypto.randomUUID();

  // A loaded node's canvas id is its Thing id (see loadPipeline); a new node ("nX") gets a fresh id so
  // the whole Thing graph can ride one id-keyed fragment upsert (create + update in place, no dupes).
  const nodeThingId = new Map<string, string>();
  for (const n of nodes)
    nodeThingId.set(n.id, model.isOf(n.id, ROLE.PipelineNode) ? n.id : crypto.randomUUID());

  const things: Array<{ Id: string; Name: string; Properties: Record<string, unknown> }> = [
    // Every pipeline the page saves is live until drafts and Publish reach the page.
    { Id: pipelineId, Name: name, Properties: { published: env(BOOLEAN, true) } },
  ];
  const relationships: Array<{ Name: string; Subject: string; Predicate: string; Target: string }> = [
    { Name: 'is', Subject: pipelineId, Predicate: isId, Target: pipelineArchetype },
  ];
  const portsToRetract: string[] = [];
  const choicesToRetract: string[] = [];
  // A stands-for relationship the node no longer means: replaced below by the one it does.
  const standsForToRetract: string[] = [];
  for (const n of nodes) {
    const thingId = nodeThingId.get(n.id)!;
    const props: Record<string, unknown> = { x: env(DOUBLE, n.x), y: env(DOUBLE, n.y) };
    if (n.parameterBindings && Object.keys(n.parameterBindings).length > 0)
      props.parameterBindings = env(STRING, JSON.stringify(n.parameterBindings));
    if (n.kind === 'output') props.when = env(STRING, n.rule ?? '');
    things.push({ Id: thingId, Name: n.label, Properties: props });
    // Boundary nodes are pipeline nodes too (so the run + editor pick them up), plus the archetype bound
    // as the pipeline's input or its output, which is what makes one a param source or a result sink.
    relationships.push({ Name: 'is', Subject: thingId, Predicate: isId, Target: nodeArchetype });
    relationships.push({ Name: 'has', Subject: pipelineId, Predicate: hasId, Target: thingId });

    if (n.kind) {
      relationships.push({ Name: 'is', Subject: thingId, Predicate: isId, Target: boundaryArchetype(n.kind)! });
      // Each declared port is written under its own identifier, so renaming it updates the same Thing; a port no
      // longer declared is retracted.
      const stale = new Set(model.boundaryPortRelationships(n.id).map((r) => r.portId));
      for (const p of n.ports) {
        stale.delete(p.portId);
        things.push({
          Id: p.portId,
          Name: p.portName,
          Properties: {
            portName: env(STRING, p.portName),
            direction: env(STRING, p.direction),
            type: env(STRING, p.type || 'any'),
            required: env(STRING, String(p.required)),
          },
        });
        relationships.push({ Name: 'is', Subject: p.portId, Predicate: isId, Target: portArchetype! });
        relationships.push({ Name: 'has', Subject: thingId, Predicate: hasId, Target: p.portId });
      }
      for (const staleId of stale) portsToRetract.push(staleId);

      const stood = model.standsFor(n.id);
      if (stood && stood.thingId !== n.standsForId) standsForToRetract.push(stood.relationshipId);
      if (n.standsForId && stood?.thingId !== n.standsForId)
        relationships.push({ Name: 'standsFor', Subject: thingId, Predicate: standsForId!, Target: n.standsForId });
    } else {
      relationships.push({ Name: 'has', Subject: thingId, Predicate: hasId, Target: n.connectionId });
      const held = new Set((n.choices ?? []).map((choice) => choice.id));
      for (const choice of n.choices ?? []) {
        things.push({ Id: choice.id, Name: choice.name, Properties: { when: env(STRING, choice.when) } });
        relationships.push({ Name: 'is', Subject: choice.id, Predicate: isId, Target: choiceArchetype! });
        relationships.push({ Name: 'has', Subject: thingId, Predicate: hasId, Target: choice.id });
      }
      for (const persisted of model.choicesOf(thingId))
        if (!held.has(persisted.id)) choicesToRetract.push(persisted.id);
    }
  }
  await modelApi.applyFragment(JSON.stringify({ Name: name, Things: things, Relationships: relationships }));

  // Wires: diff desired against persisted so edits add/remove without duplication. A wire is identified by its two
  // nodes and its two ports; the field-paths are editable properties on that same wire.
  const wireKey = (from: string, outPortId: string, to: string, inPortId: string) => `${from}|${outPortId}|${to}|${inPortId}`;
  const portNames = new Map(nodes.flatMap((n) => n.ports.map((p) => [p.portId, p.portName] as const)));
  const desired = edges
    .map((e) => ({ from: nodeThingId.get(e.source), outPortId: e.sourceHandle, to: nodeThingId.get(e.target), inPortId: e.targetHandle, fromPath: e.fromPath ?? '', toPath: e.toPath ?? '', transform: e.transform ?? '', takenOn: takenOnOf(e.takenOn, model) }))
    .filter((w): w is { from: string; outPortId: string; to: string; inPortId: string; fromPath: string; toPath: string; transform: string; takenOn: string[] } => Boolean(w.from && w.to));
  const desiredKeys = new Set(desired.map((w) => wireKey(w.from, w.outPortId, w.to, w.inPortId)));

  const persistedNodeIds = existingPipelineId
    ? model.outgoing(existingPipelineId, 'has').filter((t) => model.isOf(t.Id, ROLE.PipelineNode)).map((t) => t.Id)
    : [];
  const persistedWires = persistedNodeIds.flatMap((nid) =>
    model.outgoingWires(nid).map((w) => ({ ...w, key: wireKey(nid, w.outPortId, w.targetId, w.inPortId) })));
  const persistedByKey = new Map(persistedWires.map((w) => [w.key, w]));

  for (const w of desired) {
    const existing = persistedByKey.get(wireKey(w.from, w.outPortId, w.to, w.inPortId));
    if (existing) {
      if (w.fromPath !== existing.fromPath) await thingApi.setProperty(existing.wireId, 'fromPath', STRING, w.fromPath);
      if (w.toPath !== existing.toPath) await thingApi.setProperty(existing.wireId, 'toPath', STRING, w.toPath);
      if (w.transform !== existing.transform) await thingApi.setProperty(existing.wireId, 'transform', STRING, w.transform);
      const had = new Set(existing.takenOn.map((taken) => taken.thingId));
      for (const thingId of w.takenOn)
        if (!had.has(thingId)) await relationshipApi.create(existing.wireId, takenOnId, thingId);
      for (const taken of existing.takenOn)
        if (!w.takenOn.includes(taken.thingId)) await relationshipApi.remove(taken.relationshipId);
      continue;
    }
    const wire = await thingApi.create(`${portNames.get(w.outPortId) ?? ''} to ${portNames.get(w.inPortId) ?? ''}`, false);
    // Every value is declared at creation, empty where unset, because a Thing's property write updates and
    // does not create — so a path added later is an update rather than a call that answers "no such property".
    await thingApi.addProperty(wire.Id, 'fromPath', STRING, w.fromPath);
    await thingApi.addProperty(wire.Id, 'toPath', STRING, w.toPath);
    await thingApi.addProperty(wire.Id, 'transform', STRING, w.transform);
    await relationshipApi.create(wire.Id, isId, wireArchetype);
    await relationshipApi.create(w.from, hasId, wire.Id);
    await relationshipApi.create(wire.Id, wireId, w.to);
    await relationshipApi.create(wire.Id, inputConnector, w.outPortId);
    await relationshipApi.create(wire.Id, outputConnector, w.inPortId);
    for (const thingId of w.takenOn) await relationshipApi.create(wire.Id, takenOnId, thingId);
  }
  for (const w of persistedWires)
    if (!desiredKeys.has(w.key)) await thingApi.remove(w.wireId);

  // Ports removed from a boundary node (still on the canvas) are retracted, as is what it stood for before.
  for (const portId of portsToRetract) await thingApi.remove(portId);
  for (const choiceId of choicesToRetract) await thingApi.remove(choiceId);
  for (const relationshipId of standsForToRetract) await relationshipApi.remove(relationshipId);

  // Deleted nodes: persisted pipeline nodes no longer on the canvas are retracted, along with any port
  // child-Things a removed boundary node declared (so they don't linger as orphans).
  const desiredThingIds = new Set(nodeThingId.values());
  for (const nid of persistedNodeIds)
    if (!desiredThingIds.has(nid)) {
      for (const r of model.boundaryPortRelationships(nid)) await thingApi.remove(r.portId);
      await thingApi.remove(nid);
    }

  return { pipelineId, nodeIdMap: Object.fromEntries(nodeThingId) };
}

/** Parse a node's persisted `parameterBindings` JSON property (input-port → run-parameter key); tolerant of junk. */
function parseParameterBindings(raw: unknown): Record<string, string> | undefined {
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

export function loadPipeline(pipelineId: string, model: PipelineModel): LoadedPipeline | null {
  const pipe = model.thing(pipelineId);
  if (!pipe || !model.isOf(pipelineId, ROLE.Pipeline)) return null;

  const connectionsById = new Map(model.connections().map((c) => [c.connectionId, c]));
  const nodeThings = model.outgoing(pipelineId, 'has').filter((t) => model.isOf(t.Id, ROLE.PipelineNode));
  const nodeIds = new Set(nodeThings.map((t) => t.Id));

  const nodes: EditorNode[] = nodeThings.map((t, i) => {
    const stated = model.stated(t.Id);
    const base = {
      id: t.Id,
      label: t.Name,
      x: Number(stated.x ?? i * 280),
      y: Number(stated.y ?? 80),
      parameterBindings: parseParameterBindings(stated.parameterBindings),
    };
    // Boundary node: its ports are declared on the node itself, and it binds no connection.
    const kind = model.boundaryKind(t.Id);
    if (kind) {
      const stood = model.standsFor(t.Id);
      const rule = kind === 'output' && typeof stated.when === 'string' && stated.when.trim() !== '' ? stated.when : undefined;
      return {
        ...base, kind, connectionId: '', ports: model.boundaryPortRelationships(t.Id).map((r) => r.port),
        ...(stood ? { standsForId: stood.thingId } : {}), ...(rule !== undefined ? { rule } : {}),
      };
    }

    const conn = model.outgoing(t.Id, 'has').find((c) => model.isOf(c.Id, ROLE.Connection));
    return {
      ...base, connectionId: conn?.Id ?? '', ports: conn ? connectionsById.get(conn.Id)?.ports ?? [] : [],
      choices: model.choicesOf(t.Id).map(({ id, name, when }) => ({ id, name, when })),
    };
  });

  const edges: EditorEdge[] = [];
  for (const t of nodeThings)
    for (const wire of model.outgoingWires(t.Id)) {
      if (!nodeIds.has(wire.targetId)) continue;
      edges.push({
        id: wire.wireId,
        source: t.Id,
        sourceHandle: wire.outPortId,
        target: wire.targetId,
        targetHandle: wire.inPortId,
        fromPath: wire.fromPath || undefined,
        toPath: wire.toPath || undefined,
        transform: wire.transform || undefined,
        takenOn: wire.takenOn.map((taken) => taken.thingId),
      });
    }

  return { name: pipe.Name, nodes, edges };
}
