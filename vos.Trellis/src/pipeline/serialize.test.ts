import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../api/modelApi', () => ({ modelApi: { applyFragment: vi.fn().mockResolvedValue({}) } }));
vi.mock('../api/thingApi', () => ({ thingApi: { remove: vi.fn().mockResolvedValue({}) } }));
vi.mock('../api/relationshipApi', () => ({
  relationshipApi: {
    create: vi.fn().mockResolvedValue({ Id: 'new-rel' }),
    setProperty: vi.fn().mockResolvedValue({}),
    remove: vi.fn().mockResolvedValue({}),
  },
}));

import type { VosThing, VosRelationship } from '../types/vos';
import { PipelineModel } from './model';
import { savePipeline, loadPipeline, type EditorNode } from './serialize';
import { modelApi } from '../api/modelApi';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';

const applyFragment = vi.mocked(modelApi.applyFragment);
const thingRemove = vi.mocked(thingApi.remove);
const relRemove = vi.mocked(relationshipApi.remove);
const relCreate = vi.mocked(relationshipApi.create);

// A model with the pipeline archetypes/predicates, one connection, and (optionally) an existing pipeline
// P with two nodes N1,N2 and a wire N1.out -> N2.in — enough to exercise the in-place-update diff.
function buildModel() {
  const things: VosThing[] = [];
  const rels: VosRelationship[] = [];
  let n = 0;
  const T = (id: string, name: string, props: Record<string, unknown> = {}): VosThing => {
    const t = { Id: id, Name: name, Properties: props };
    things.push(t);
    return t;
  };
  const R = (s: string, p: string, t: string, props: Record<string, unknown> = {}) =>
    rels.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: props });

  const is = T('is', 'is'), has = T('has', 'has'), feeds = T('feeds', 'feeds');
  T('Pipeline', 'Pipeline'); T('PipelineNode', 'PipelineNode'); T('PlatformServiceConnection', 'PlatformServiceConnection');
  T('Service', 'Service'); T('Port', 'Port'); T('PipelineWire', 'PipelineWire');
  // Boundary-node archetypes (#5873); each is-a PipelineNode so the node collection picks its instances up.
  T('PipelineInput', 'PipelineInput'); R('PipelineInput', 'is', 'PipelineNode');
  T('PipelineOutput', 'PipelineOutput'); R('PipelineOutput', 'is', 'PipelineNode');
  R('feeds', 'is', 'PipelineWire');

  T('svc', 'svc'); R('svc', 'is', 'Service');
  T('conn', 'conn', { Subdomain: 'echo' }); R('conn', 'is', 'PlatformServiceConnection'); R('conn', 'has', 'svc');

  // Existing pipeline P: N1 --feeds(out->in)--> N2
  T('P', 'MyPipeline'); R('P', 'is', 'Pipeline');
  T('N1', 'Node1'); R('N1', 'is', 'PipelineNode'); R('N1', 'has', 'conn'); R('P', 'has', 'N1');
  T('N2', 'Node2'); R('N2', 'is', 'PipelineNode'); R('N2', 'has', 'conn'); R('P', 'has', 'N2');
  R('N1', 'feeds', 'N2', { fromPort: 'out', toPort: 'in' });

  return { model: new PipelineModel(things, rels), ids: { conn: 'conn' }, is, has, feeds };
}

const node = (id: string, label: string, connectionId = 'conn'): EditorNode =>
  ({ id, label, connectionId, x: 10, y: 20, ports: [] });

beforeEach(() => vi.clearAllMocks());

describe('savePipeline — create (no existing pipeline id)', () => {
  it('upserts a new pipeline via one fragment and deletes nothing', async () => {
    const { model } = buildModel();
    const saved = await savePipeline('Fresh', [node('n1', 'A')], [], model);

    expect(applyFragment).toHaveBeenCalledOnce();
    const frag = JSON.parse(applyFragment.mock.calls[0][0]);
    expect(frag.Things.some((t: { Name: string }) => t.Name === 'Fresh')).toBe(true);
    expect(frag.Things.some((t: { Name: string }) => t.Name === 'A')).toBe(true);
    expect(thingRemove).not.toHaveBeenCalled();
    // The new pipeline got a fresh id (not one of the fixture ids).
    expect(saved.pipelineId).not.toBe('P');
  });
});

describe('savePipeline — update in place (existing pipeline id)', () => {
  it('reuses the pipeline id and keeps an existing node’s Thing id (no duplicate)', async () => {
    const { model } = buildModel();
    // Keep only N1 (its canvas id IS its Thing id), moved; drop N2.
    const saved = await savePipeline('MyPipeline', [{ ...node('N1', 'Node1'), x: 99 }], [], model, 'P');

    expect(saved.pipelineId).toBe('P');
    const frag = JSON.parse(applyFragment.mock.calls[0][0]);
    const pipeline = frag.Things.find((t: { Id: string }) => t.Id === 'P');
    expect(pipeline).toBeDefined();
    const n1 = frag.Things.find((t: { Id: string }) => t.Id === 'N1');
    expect(n1).toBeDefined();
    expect(n1.Properties.x.value).toBe(99);
  });

  it('retracts a node removed from the canvas', async () => {
    const { model } = buildModel();
    await savePipeline('MyPipeline', [node('N1', 'Node1')], [], model, 'P');
    expect(thingRemove).toHaveBeenCalledWith('N2');
  });

  it('removes a wire that is gone and creates a wire that is new', async () => {
    const { model } = buildModel();
    // Drop the N1->N2 wire (no edges) — the one persisted wire should be removed, and no others.
    await savePipeline('MyPipeline', [node('N1', 'Node1'), node('N2', 'Node2')], [], model, 'P');
    expect(relRemove).toHaveBeenCalledTimes(1);
  });

  it('creates a new wire with its fromPort/toPort', async () => {
    const { model } = buildModel();
    const edges = [{ id: 'e1', source: 'N2', sourceHandle: 'out', target: 'N1', targetHandle: 'in' }];
    await savePipeline('MyPipeline', [node('N1', 'Node1'), node('N2', 'Node2')], edges, model, 'P');
    // N2->N1 is new (fixture only had N1->N2), so a wire is created + its ports set.
    expect(relCreate).toHaveBeenCalledWith('N2', 'feeds', 'N1');
    expect(relationshipApi.setProperty).toHaveBeenCalledWith('new-rel', 'fromPort', 'vos.String', 'out');
  });
});

// Boundary I/O nodes (#5873) --------------------------------------------------------------------------

type Frag = {
  Things: { Id: string; Name: string; Properties: Record<string, unknown> }[];
  Relationships: { Name: string; Subject: string; Predicate: string; Target: string }[];
};

describe('savePipeline — boundary nodes (#5873)', () => {
  it('persists an Input node as a PipelineInput + PipelineNode with its declared output port, and no connection', async () => {
    const { model } = buildModel();
    const inputNode: EditorNode = {
      id: 'nin', label: 'Input', connectionId: '', x: 0, y: 0, kind: 'input',
      ports: [{ portName: 'seed', direction: 'out', type: 'any', required: false }],
    };

    await savePipeline('B', [inputNode], [], model);

    const frag: Frag = JSON.parse(applyFragment.mock.calls[0][0]);
    const nodeThing = frag.Things.find((t) => t.Name === 'Input')!;
    const isTargets = frag.Relationships.filter((r) => r.Subject === nodeThing.Id && r.Name === 'is').map((r) => r.Target);
    expect(isTargets).toContain('PipelineInput');
    expect(isTargets).toContain('PipelineNode');

    // Declares a Port child (has → Port, direction out) …
    const portThing = frag.Things.find((t) => t.Name === 'seed')!;
    expect(portThing).toBeTruthy();
    expect(frag.Relationships.some((r) => r.Subject === portThing.Id && r.Name === 'is' && r.Target === 'Port')).toBe(true);
    expect(frag.Relationships.some((r) => r.Subject === nodeThing.Id && r.Name === 'has' && r.Target === portThing.Id)).toBe(true);
    // … and binds NO connection.
    expect(frag.Relationships.some((r) => r.Subject === nodeThing.Id && r.Name === 'has' && r.Target === 'conn')).toBe(false);
  });
});

describe('loadPipeline — boundary nodes (#5873)', () => {
  it('reconstructs a boundary node kind and its declared ports', () => {
    // A model with a pipeline BP → an Output boundary node OUT that declares an input port `result`.
    const things: VosThing[] = [];
    const rels: VosRelationship[] = [];
    let n = 0;
    const T = (id: string, name: string, props: Record<string, unknown> = {}) => { things.push({ Id: id, Name: name, Properties: props }); };
    const R = (s: string, p: string, t: string, props: Record<string, unknown> = {}) =>
      rels.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: props });

    T('is', 'is'); T('has', 'has');
    T('Pipeline', 'Pipeline'); T('PipelineNode', 'PipelineNode'); T('Port', 'Port');
    T('PipelineOutput', 'PipelineOutput'); R('PipelineOutput', 'is', 'PipelineNode');
    T('BP', 'Boundary'); R('BP', 'is', 'Pipeline');
    T('OUT', 'Output'); R('OUT', 'is', 'PipelineOutput'); R('BP', 'has', 'OUT');
    T('OUT.result', 'result', { portName: 'result', direction: 'in', type: 'any', required: 'true' });
    R('OUT.result', 'is', 'Port'); R('OUT', 'has', 'OUT.result');

    const loaded = loadPipeline('BP', new PipelineModel(things, rels))!;
    const out = loaded.nodes.find((node) => node.id === 'OUT')!;
    expect(out.kind).toBe('output');
    expect(out.connectionId).toBe('');
    expect(out.ports.map((p) => p.portName)).toContain('result');
    expect(out.ports.find((p) => p.portName === 'result')!.direction).toBe('in');
  });
});

// Field-level wire mapping (#5874) --------------------------------------------------------------------

describe('savePipeline — wire field-paths (#5874)', () => {
  it('persists a new wire’s fromPath and toPath', async () => {
    const { model } = buildModel();
    const edges = [{ id: 'e', source: 'na', sourceHandle: 'out', target: 'nb', targetHandle: 'in', fromPath: 'user.id', toPath: 'a' }];
    await savePipeline('FM', [node('na', 'A'), node('nb', 'B')], edges, model);
    expect(relationshipApi.setProperty).toHaveBeenCalledWith('new-rel', 'fromPath', 'vos.String', 'user.id');
    expect(relationshipApi.setProperty).toHaveBeenCalledWith('new-rel', 'toPath', 'vos.String', 'a');
  });

  it('does not write a field-path when it is empty (whole-payload wire)', async () => {
    const { model } = buildModel();
    const edges = [{ id: 'e', source: 'na', sourceHandle: 'out', target: 'nb', targetHandle: 'in' }];
    await savePipeline('FM', [node('na', 'A'), node('nb', 'B')], edges, model);
    expect(relationshipApi.setProperty).not.toHaveBeenCalledWith('new-rel', 'fromPath', 'vos.String', '');
    expect(relationshipApi.setProperty).not.toHaveBeenCalledWith('new-rel', 'toPath', 'vos.String', '');
  });
});

describe('loadPipeline — wire field-paths (#5874)', () => {
  it('reconstructs a wire’s fromPath and toPath', () => {
    const things: VosThing[] = [];
    const rels: VosRelationship[] = [];
    let n = 0;
    const T = (id: string, name: string, props: Record<string, unknown> = {}) => { things.push({ Id: id, Name: name, Properties: props }); };
    const R = (s: string, p: string, t: string, props: Record<string, unknown> = {}) =>
      rels.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: props });

    T('is', 'is'); T('has', 'has'); T('feeds', 'feeds');
    T('Pipeline', 'Pipeline'); T('PipelineNode', 'PipelineNode'); T('PipelineWire', 'PipelineWire');
    R('feeds', 'is', 'PipelineWire');
    T('FP', 'FieldPipe'); R('FP', 'is', 'Pipeline');
    T('N1', 'N1'); R('N1', 'is', 'PipelineNode'); R('FP', 'has', 'N1');
    T('N2', 'N2'); R('N2', 'is', 'PipelineNode'); R('FP', 'has', 'N2');
    R('N1', 'feeds', 'N2', { fromPort: 'out', toPort: 'in', fromPath: 'user.id', toPath: 'a' });

    const loaded = loadPipeline('FP', new PipelineModel(things, rels))!;
    const edge = loaded.edges[0];
    expect(edge.fromPath).toBe('user.id');
    expect(edge.toPath).toBe('a');
  });
});

// On-wire JSONata transforms (#5875) ------------------------------------------------------------------

describe('savePipeline / loadPipeline — wire transform (#5875)', () => {
  it('persists a new wire’s transform', async () => {
    const { model } = buildModel();
    const edges = [{ id: 'e', source: 'na', sourceHandle: 'out', target: 'nb', targetHandle: 'in', transform: '{"name": firstName}' }];
    await savePipeline('T', [node('na', 'A'), node('nb', 'B')], edges, model);
    expect(relationshipApi.setProperty).toHaveBeenCalledWith('new-rel', 'transform', 'vos.String', '{"name": firstName}');
  });

  it('reconstructs a wire’s transform on load', () => {
    const things: VosThing[] = [];
    const rels: VosRelationship[] = [];
    let n = 0;
    const T = (id: string, name: string, props: Record<string, unknown> = {}) => { things.push({ Id: id, Name: name, Properties: props }); };
    const R = (s: string, p: string, t: string, props: Record<string, unknown> = {}) =>
      rels.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: props });

    T('is', 'is'); T('has', 'has'); T('feeds', 'feeds');
    T('Pipeline', 'Pipeline'); T('PipelineNode', 'PipelineNode'); T('PipelineWire', 'PipelineWire');
    R('feeds', 'is', 'PipelineWire');
    T('TP', 'TransformPipe'); R('TP', 'is', 'Pipeline');
    T('N1', 'N1'); R('N1', 'is', 'PipelineNode'); R('TP', 'has', 'N1');
    T('N2', 'N2'); R('N2', 'is', 'PipelineNode'); R('TP', 'has', 'N2');
    R('N1', 'feeds', 'N2', { fromPort: 'out', toPort: 'in', transform: '{"x": y}' });

    const loaded = loadPipeline('TP', new PipelineModel(things, rels))!;
    expect(loaded.edges[0].transform).toBe('{"x": y}');
  });
});
