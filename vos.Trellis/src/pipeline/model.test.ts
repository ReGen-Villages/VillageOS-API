import { describe, it, expect } from 'vitest';
import type { VosThing, VosRelationship } from '../types/vos';
import { PipelineModel, typesCompatible } from './model';
import { loadPipeline } from './serialize';

// Build the demo model (Generate –feeds(echo→message)→ Echo), node -has-> Connection[subdomain] -has-> Service.
function demoModel(): { model: PipelineModel; pipelineId: string } {
  const things: VosThing[] = [];
  const rels: VosRelationship[] = [];
  let n = 0;
  const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
    const t = { Id: `t${++n}`, Name: name, Properties: props };
    things.push(t);
    return t;
  };
  const R = (s: string, p: string, t: string, props: Record<string, unknown> = {}) =>
    rels.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: props });

  const is = T('is'), has = T('has'), feeds = T('feeds');
  const pipelineA = T('Pipeline'), nodeA = T('PipelineNode'), connA = T('PlatformServiceConnection'),
    svcA = T('Service'), portA = T('Port'), wireA = T('PipelineWire');
  R(feeds.Id, is.Id, wireA.Id);

  const proto = T('EchoProto');
  R(proto.Id, is.Id, svcA.Id);
  const pIn = T('p.in', { direction: 'in', type: 'string', portName: 'message', required: 'true' });
  const pOut = T('p.out', { direction: 'out', type: 'string', portName: 'echo' });
  R(pIn.Id, is.Id, portA.Id);
  R(pOut.Id, is.Id, portA.Id);
  R(proto.Id, has.Id, pIn.Id);
  R(proto.Id, has.Id, pOut.Id);

  const genSvc = T('genSvc'), echSvc = T('echSvc');
  R(genSvc.Id, is.Id, proto.Id);
  R(echSvc.Id, is.Id, proto.Id);
  const genConn = T('genConn', { Subdomain: 'generate' }), echConn = T('echConn', { Subdomain: 'echo' });
  R(genConn.Id, is.Id, connA.Id);
  R(echConn.Id, is.Id, connA.Id);
  R(genConn.Id, has.Id, genSvc.Id);
  R(echConn.Id, has.Id, echSvc.Id);

  const gen = T('Generate'), ech = T('Echo');
  R(gen.Id, is.Id, nodeA.Id);
  R(ech.Id, is.Id, nodeA.Id);
  R(gen.Id, has.Id, genConn.Id);
  R(ech.Id, has.Id, echConn.Id);

  const pipe = T('Demo');
  R(pipe.Id, is.Id, pipelineA.Id);
  R(pipe.Id, has.Id, gen.Id);
  R(pipe.Id, has.Id, ech.Id);
  R(gen.Id, feeds.Id, ech.Id, { fromPort: 'echo', toPort: 'message' });

  return { model: new PipelineModel(things, rels), pipelineId: pipe.Id };
}

describe('PipelineModel', () => {
  it('lists dispatchable connections with subdomain and resolved ports', () => {
    const { model } = demoModel();
    const conns = model.connections();
    expect(conns.map((c) => c.subdomain).sort()).toEqual(['echo', 'generate']);
    const gen = conns.find((c) => c.subdomain === 'generate')!;
    expect(gen.ports.find((p) => p.portName === 'message')?.direction).toBe('in');
    expect(gen.ports.find((p) => p.portName === 'echo')?.direction).toBe('out');
  });

  it('identifies the wire predicate by archetype, not by name', () => {
    const { model } = demoModel();
    expect(model.wirePredicateId()).toBe(model.predicateIdByName('feeds'));
  });
});

describe('typesCompatible', () => {
  it('equal types and wildcards match; different concrete types do not', () => {
    expect(typesCompatible('string', 'string')).toBe(true);
    expect(typesCompatible('', 'string')).toBe(true);
    expect(typesCompatible('any', 'number')).toBe(true);
    expect(typesCompatible('string', 'number')).toBe(false);
  });
});

describe('run animation source (#5635)', () => {
  it('reads overall run status and per-node status from the live model', () => {
    const things: VosThing[] = [];
    const rels: VosRelationship[] = [];
    let n = 0;
    const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
      const t = { Id: `t${++n}`, Name: name, Properties: props };
      things.push(t);
      return t;
    };
    const has = T('has');
    // Phloem records: run -has-> NodeRun, each NodeRun carrying nodeId + status.
    const run = T('PipelineRun abc', { status: 'running' });
    const nr1 = T('NodeRun Generate', { nodeId: 'node-gen', status: 'succeeded' });
    const nr2 = T('NodeRun Echo', { nodeId: 'node-ech', status: 'running' });
    rels.push({ Id: 'rr1', Name: '', SubjectId: run.Id, PredicateId: has.Id, TargetId: nr1.Id, Properties: {} });
    rels.push({ Id: 'rr2', Name: '', SubjectId: run.Id, PredicateId: has.Id, TargetId: nr2.Id, Properties: {} });

    const model = new PipelineModel(things, rels);
    expect(model.runStatus(run.Id)).toBe('running');
    expect(model.nodeRunStatuses(run.Id)).toEqual({ 'node-gen': 'succeeded', 'node-ech': 'running' });
    expect(model.runStatus('missing')).toBeUndefined();
    expect(model.nodeRunStatuses('missing')).toEqual({});
  });

  it('separates the aggregate ring status from per-item fan-out progress (#5648)', () => {
    const things: VosThing[] = [];
    const rels: VosRelationship[] = [];
    let n = 0;
    const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
      const t = { Id: `t${++n}`, Name: name, Properties: props };
      things.push(t);
      return t;
    };
    const has = T('has');
    const run = T('PipelineRun', { status: 'running' });
    const agg = T('NodeRun Scorer', { nodeId: 'node-score', status: 'running' }); // aggregate — no index
    const i0 = T('NodeRun Scorer #0', { nodeId: 'node-score', status: 'succeeded', index: '0', total: '3' });
    const i1 = T('NodeRun Scorer #1', { nodeId: 'node-score', status: 'succeeded', index: '1', total: '3' });
    const i2 = T('NodeRun Scorer #2', { nodeId: 'node-score', status: 'running', index: '2', total: '3' });
    for (const nr of [agg, i0, i1, i2])
      rels.push({ Id: `r${++n}`, Name: '', SubjectId: run.Id, PredicateId: has.Id, TargetId: nr.Id, Properties: {} });

    const model = new PipelineModel(things, rels);
    expect(model.nodeRunStatuses(run.Id)).toEqual({ 'node-score': 'running' });        // ring = aggregate only
    expect(model.nodeRunProgress(run.Id)).toEqual({ 'node-score': { done: 2, total: 3 } }); // 2 of 3 items terminal
  });

  it('lists a pipeline\'s runs newest-first, scoped to that pipeline', () => {
    const things: VosThing[] = [];
    const rels: VosRelationship[] = [];
    let n = 0;
    const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
      const t = { Id: `t${++n}`, Name: name, Properties: props };
      things.push(t);
      return t;
    };
    const is = T('is'), of = T('of'), runArch = T('PipelineRun');
    const pipeA = T('Pipeline A'), pipeB = T('Pipeline B');
    const rel = (s: string, p: string, t: string) =>
      rels.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} });

    // Two runs of A (different times) + one run of B — B must not leak into A's history.
    const a1 = T('PipelineRun a1', { status: 'succeeded', startedUtc: '2026-06-23T10:00:00Z' });
    const a2 = T('PipelineRun a2', { status: 'failed', startedUtc: '2026-06-23T12:00:00Z' });
    const b1 = T('PipelineRun b1', { status: 'succeeded', startedUtc: '2026-06-23T11:00:00Z' });
    for (const r of [a1, a2, b1]) rel(r.Id, is.Id, runArch.Id);
    rel(a1.Id, of.Id, pipeA.Id);
    rel(a2.Id, of.Id, pipeA.Id);
    rel(b1.Id, of.Id, pipeB.Id);

    const model = new PipelineModel(things, rels);
    const runs = model.runsOf(pipeA.Id);
    expect(runs.map((r) => r.runId)).toEqual([a2.Id, a1.Id]); // newest first, B excluded
    expect(runs[0]).toMatchObject({ status: 'failed', startedUtc: '2026-06-23T12:00:00Z' });
    expect(model.runsOf('missing')).toEqual([]);
  });
});

describe('loadPipeline', () => {
  it('reconstructs nodes, connections, and the wire from the model', () => {
    const { model, pipelineId } = demoModel();
    const loaded = loadPipeline(pipelineId, model)!;
    expect(loaded.name).toBe('Demo');
    expect(loaded.nodes.map((n) => n.label).sort()).toEqual(['Echo', 'Generate']);
    expect(loaded.edges).toHaveLength(1);
    expect(loaded.edges[0]).toMatchObject({ sourceHandle: 'echo', targetHandle: 'message' });
  });

  it('returns null for a non-pipeline thing', () => {
    const { model } = demoModel();
    expect(loadPipeline('does-not-exist', model)).toBeNull();
  });

  it('parses a node\'s paramBindings property (#5647)', () => {
    const things: VosThing[] = [];
    const rels: VosRelationship[] = [];
    let n = 0;
    const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
      const t = { Id: `t${++n}`, Name: name, Properties: props };
      things.push(t);
      return t;
    };
    const rel = (s: string, p: string, t: string) =>
      rels.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} });
    const is = T('is'), has = T('has'), pipeArch = T('Pipeline'), nodeArch = T('PipelineNode'), connArch = T('PlatformServiceConnection');
    const conn = T('conn', { Subdomain: 'x' });
    rel(conn.Id, is.Id, connArch.Id);
    const node = T('N', { paramBindings: '{"message":"greeting"}' });
    rel(node.Id, is.Id, nodeArch.Id);
    rel(node.Id, has.Id, conn.Id);
    const pipe = T('P');
    rel(pipe.Id, is.Id, pipeArch.Id);
    rel(pipe.Id, has.Id, node.Id);

    const loaded = loadPipeline(pipe.Id, new PipelineModel(things, rels))!;
    expect(loaded.nodes[0].paramBindings).toEqual({ message: 'greeting' });
  });
});
