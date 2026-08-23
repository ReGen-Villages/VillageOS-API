/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { VosThing, VosRelationship } from '../types/vos';
import { PipelineModel, ARCHETYPE_FLAG, typesCompatible } from './model';
import { loadPipeline } from './serialize';

/** An archetype's own mark, which is the only thing that says what role it plays. */
const marked = (roleFlag: string): Record<string, unknown> => ({ [roleFlag]: true });

// Build the demo model (Generate –feeds(echo→message)→ Echo), node -has-> Connection[subdomain] -has-> Service.
// Every archetype here is named something the editor has never heard of and says what it is by the flag it
// carries, so a fixture that resolves at all proves nothing is found by name (#6530).
function demoModel(): { model: PipelineModel; pipelineId: string; pipelineArchetypeId: string } {
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
  const pipelineA = T('Workflow', marked(ARCHETYPE_FLAG.Pipeline)),
    nodeA = T('Step', marked(ARCHETYPE_FLAG.PipelineNode)),
    connA = T('Endpoint', marked(ARCHETYPE_FLAG.Connection)),
    svcA = T('Capability', marked(ARCHETYPE_FLAG.Service)),
    portA = T('Socket', marked(ARCHETYPE_FLAG.Port)),
    wireA = T('Link', marked(ARCHETYPE_FLAG.PipelineWire));
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

  return { model: new PipelineModel(things, rels), pipelineId: pipe.Id, pipelineArchetypeId: pipelineA.Id };
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

  it('identifies the wire predicate by the mark its archetype carries, not by name', () => {
    const { model } = demoModel();
    expect(model.wirePredicateId()).toBe(model.predicateIdByName('feeds'));
  });

  it('gives the archetype a save writes its `is` edge to, whatever the model calls it', () => {
    const { model, pipelineArchetypeId } = demoModel();
    expect(model.archetypeCarrying(ARCHETYPE_FLAG.Pipeline)).toBe(pipelineArchetypeId);
    expect(model.archetypeCarrying('__IsNothingAnyoneMarks')).toBeUndefined();
  });

  it('does not count an archetype as playing its own role, so the picker lists pipelines and not the archetype', () => {
    const { model, pipelineId, pipelineArchetypeId } = demoModel();
    expect(model.isOfArchetypeCarrying(pipelineId, ARCHETYPE_FLAG.Pipeline)).toBe(true);
    expect(model.isOfArchetypeCarrying(pipelineArchetypeId, ARCHETYPE_FLAG.Pipeline)).toBe(false);
  });

  it('reaches a marked ancestor by either route, and gives up on an `is` cycle instead of walking forever', () => {
    const things: VosThing[] = [];
    const rels: VosRelationship[] = [];
    let n = 0;
    const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
      const t = { Id: `t${++n}`, Name: name, Properties: props };
      things.push(t);
      return t;
    };
    const is = T('is');
    const rel = (s: string, t: string) =>
      rels.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: is.Id, TargetId: t, Properties: {} });

    // A node under two archetypes that share one marked ancestor: it is reached twice, answered once.
    const root = T('Step', marked(ARCHETYPE_FLAG.PipelineNode)), left = T('Left'), right = T('Right');
    const node = T('N');
    rel(left.Id, root.Id);
    rel(right.Id, root.Id);
    rel(node.Id, left.Id);
    rel(node.Id, right.Id);
    // A pair of archetypes that `is` each other, which no reader may follow round.
    const loopA = T('LoopA'), loopB = T('LoopB'), inLoop = T('InLoop');
    rel(loopA.Id, loopB.Id);
    rel(loopB.Id, loopA.Id);
    rel(inLoop.Id, loopA.Id);

    const model = new PipelineModel(things, rels);
    expect(model.isOfArchetypeCarrying(node.Id, ARCHETYPE_FLAG.PipelineNode)).toBe(true);
    expect(model.isOfArchetypeCarrying(inLoop.Id, ARCHETYPE_FLAG.PipelineNode)).toBe(false);
  });
});

describe('the vocabulary the editor holds', () => {
  const here = dirname(fileURLToPath(import.meta.url));
  const editorSources = {
    'pipeline/model.ts': join(here, 'model.ts'),
    'pipeline/serialize.ts': join(here, 'serialize.ts'),
    'pages/PipelinePage.tsx': join(here, '..', 'pages', 'PipelinePage.tsx'),
  };

  // What the seed tool happens to call these archetypes. A model may call them anything at all, which is
  // why the editor holds none of these words (#6530).
  const NAMES_A_MODEL_MAY_CHANGE = [
    'Pipeline', 'PipelineNode', 'PipelineInput', 'PipelineOutput', 'PlatformServiceConnection',
    'Service', 'Port', 'PipelineWire', 'PipelineRun', 'NodeRun',
  ];

  for (const [file, path] of Object.entries(editorSources))
    it(`${file} spells no archetype name`, () => {
      const source = readFileSync(path, 'utf-8');
      const held = NAMES_A_MODEL_MAY_CHANGE.filter((name) => new RegExp(`['"\`]${name}['"\`]`).test(source));
      expect(held).toEqual([]);
    });

  it('reads the same marks the orchestrator does', () => {
    const phloem = readFileSync(
      join(here, '..', '..', '..', 'vos.Service.Phloem', 'Model', 'PipelineArchetypes.cs'),
      'utf-8',
    );
    for (const flag of Object.values(ARCHETYPE_FLAG)) expect(phloem).toContain(`"${flag}"`);
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
    const is = T('is'), of = T('of'), runArch = T('Execution', marked(ARCHETYPE_FLAG.PipelineRun));
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
    const is = T('is'), has = T('has'), pipeArch = T('Workflow', marked(ARCHETYPE_FLAG.Pipeline)),
      nodeArch = T('Step', marked(ARCHETYPE_FLAG.PipelineNode)),
      connArch = T('Endpoint', marked(ARCHETYPE_FLAG.Connection));
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
