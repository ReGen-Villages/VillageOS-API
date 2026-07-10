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
import { savePipeline, type EditorNode } from './serialize';
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
  R('feeds', 'is', 'PipelineWire');

  const svc = T('svc', 'svc'); R('svc', 'is', 'Service');
  const conn = T('conn', 'conn', { Subdomain: 'echo' }); R('conn', 'is', 'PlatformServiceConnection'); R('conn', 'has', 'svc');

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
