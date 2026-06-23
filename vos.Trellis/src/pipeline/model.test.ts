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
  const pipelineA = T('Pipeline'), nodeA = T('PipelineNode'), connA = T('Connection'),
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
});
