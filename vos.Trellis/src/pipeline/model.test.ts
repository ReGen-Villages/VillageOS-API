/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync } from 'node:fs';
import { basename, join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { VosThing, VosRelationship } from '../types/vos';
import { PipelineModel, ARCHETYPE_FLAG, PREDICATE_FLAG, typesCompatible } from './model';
import { loadPipeline } from './serialize';

/** An archetype's own mark, which is the only thing that says what role it plays. */
const marked = (roleFlag: string): Record<string, unknown> => ({ [roleFlag]: true });

// Build the demo model (Generate –carries(echo→message)→ Echo), node -has-> connection[subdomain] -has-> service.
// Every archetype here is named something the editor has never heard of and says what it is by the flag it
// carries, so a fixture that resolves at all proves nothing is found by name.
function demoModel(): { model: PipelineModel; pipelineId: string; pipelineArchetypeId: string } {
  const things: VosThing[] = [];
  const relationships: VosRelationship[] = [];
  let n = 0;
  const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
    const t = { Id: `t${++n}`, Name: name, Properties: props };
    things.push(t);
    return t;
  };
  const R = (s: string, p: string, t: string, props: Record<string, unknown> = {}) =>
    relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: props });

  const is = T('is'), has = T('has'), carries = T('carries');
  const pipelineA = T('Workflow', marked(ARCHETYPE_FLAG.Pipeline)),
    nodeA = T('Step', marked(ARCHETYPE_FLAG.PipelineNode)),
    connA = T('Endpoint', marked(ARCHETYPE_FLAG.Connection)),
    serviceA = T('Capability', marked(ARCHETYPE_FLAG.Service)),
    portA = T('Socket', marked(ARCHETYPE_FLAG.Port)),
    wireA = T('Link', marked(ARCHETYPE_FLAG.PipelineWire));
  R(carries.Id, is.Id, wireA.Id);

  const proto = T('EchoProto');
  R(proto.Id, is.Id, serviceA.Id);
  const pIn = T('p.in', { direction: 'in', type: 'string', portName: 'message', required: 'true' });
  const pOut = T('p.out', { direction: 'out', type: 'string', portName: 'echo' });
  R(pIn.Id, is.Id, portA.Id);
  R(pOut.Id, is.Id, portA.Id);
  R(proto.Id, has.Id, pIn.Id);
  R(proto.Id, has.Id, pOut.Id);

  const generatorService = T('genSvc'), echoService = T('echSvc');
  R(generatorService.Id, is.Id, proto.Id);
  R(echoService.Id, is.Id, proto.Id);
  const genConn = T('genConn', { Subdomain: 'generate' }), echConn = T('echConn', { Subdomain: 'echo' });
  R(genConn.Id, is.Id, connA.Id);
  R(echConn.Id, is.Id, connA.Id);
  R(genConn.Id, has.Id, generatorService.Id);
  R(echConn.Id, has.Id, echoService.Id);

  const gen = T('Generate'), ech = T('Echo');
  R(gen.Id, is.Id, nodeA.Id);
  R(ech.Id, is.Id, nodeA.Id);
  R(gen.Id, has.Id, genConn.Id);
  R(ech.Id, has.Id, echConn.Id);

  const pipe = T('Demo');
  R(pipe.Id, is.Id, pipelineA.Id);
  R(pipe.Id, has.Id, gen.Id);
  R(pipe.Id, has.Id, ech.Id);
  R(gen.Id, carries.Id, ech.Id, { fromPort: 'echo', toPort: 'message' });

  return { model: new PipelineModel(things, relationships), pipelineId: pipe.Id, pipelineArchetypeId: pipelineA.Id };
}

// The same two nodes, wired by Things rather than relationships, and wired twice — the case a relationship cannot
// express, because the model refuses a second relationship on one subject, predicate and target. The wires are
// listed before the predicate on purpose: a snapshot's order is arbitrary, and both are of the wire
// archetype, so anything picking "the first of that archetype" picks a wire here.
function heldWireModel(): { model: PipelineModel; carriesId: string } {
  const things: VosThing[] = [];
  const relationships: VosRelationship[] = [];
  let n = 0;
  const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
    const t = { Id: `t${++n}`, Name: name, Properties: props };
    things.push(t);
    return t;
  };
  const R = (s: string, p: string, t: string, props: Record<string, unknown> = {}) =>
    relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: props });

  const is = T('is'), has = T('has');
  const nodeA = T('Step', marked(ARCHETYPE_FLAG.PipelineNode));
  const wireA = T('Link', marked(ARCHETYPE_FLAG.PipelineWire));

  const wireOne = T('w.echo', { fromPort: 'echo', toPort: 'message' });
  const wireTwo = T('w.trace', { fromPort: 'trace', toPort: 'context' });
  const carries = T('carries');
  // Both shapes are read while one replaces the other, so the predicate keeps its own mark: this model
  // holds three Things of the wire archetype and only one of them is the predicate.
  R(carries.Id, is.Id, wireA.Id);

  const gen = T('Generate'), ech = T('Echo');
  R(gen.Id, is.Id, nodeA.Id);
  R(ech.Id, is.Id, nodeA.Id);

  for (const wire of [wireOne, wireTwo]) {
    R(wire.Id, is.Id, wireA.Id);
    R(gen.Id, has.Id, wire.Id);
    R(wire.Id, carries.Id, ech.Id);
  }

  return { model: new PipelineModel(things, relationships), carriesId: carries.Id };
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
    expect(model.wirePredicateId()).toBe(model.predicateIdByName('carries'));
  });

  it('does not mistake a wire held as a Thing for the wire predicate, whichever the model lists first', () => {
    const { model, carriesId } = heldWireModel();
    expect(model.wirePredicateId()).toBe(carriesId);
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
    const relationships: VosRelationship[] = [];
    let n = 0;
    const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
      const t = { Id: `t${++n}`, Name: name, Properties: props };
      things.push(t);
      return t;
    };
    const is = T('is');
    const relationship = (s: string, t: string) =>
      relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: is.Id, TargetId: t, Properties: {} });

    // A node under two archetypes that share one marked ancestor: it is reached twice, answered once.
    const root = T('Step', marked(ARCHETYPE_FLAG.PipelineNode)), left = T('Left'), right = T('Right');
    const node = T('N');
    relationship(left.Id, root.Id);
    relationship(right.Id, root.Id);
    relationship(node.Id, left.Id);
    relationship(node.Id, right.Id);
    // A pair of archetypes that `is` each other, which no reader may follow round.
    const loopA = T('LoopA'), loopB = T('LoopB'), inLoop = T('InLoop');
    relationship(loopA.Id, loopB.Id);
    relationship(loopB.Id, loopA.Id);
    relationship(inLoop.Id, loopA.Id);

    const model = new PipelineModel(things, relationships);
    expect(model.isOfArchetypeCarrying(node.Id, ARCHETYPE_FLAG.PipelineNode)).toBe(true);
    expect(model.isOfArchetypeCarrying(inLoop.Id, ARCHETYPE_FLAG.PipelineNode)).toBe(false);
  });
});

describe('the vocabulary the editor holds', () => {
  const here = dirname(fileURLToPath(import.meta.url));
  // Every source of the editor, found rather than listed, so a file added later is held to this too.
  const editorSources = [
    ...readdirSync(here).filter((file) => file.endsWith('.ts') && !file.endsWith('.test.ts')).map((file) => join(here, file)),
    join(here, '..', 'pages', 'PipelinePage.tsx'),
  ];

  // What the seed tool happens to call these archetypes. A model may call them anything at all, which is
  // why the editor holds none of these words.
  const NAMES_A_MODEL_MAY_CHANGE = [
    'Pipeline', 'PipelineNode', 'PipelineInput', 'PipelineOutput', 'PlatformServiceConnection',
    'Service', 'Port', 'PipelineWire', 'PipelineRun', 'NodeRun',
  ];

  for (const path of editorSources)
    it(`${basename(path)} spells no archetype name`, () => {
      const source = readFileSync(path, 'utf-8');
      const held = NAMES_A_MODEL_MAY_CHANGE.filter((name) => new RegExp(`['"\`]${name}['"\`]`).test(source));
      expect(held).toEqual([]);
    });

  /** The marks the page reads that the orchestrator does not: the range mark is the platform's own,
   *  and the orchestrator does not yet act on an external system or a kind of message. */
  const READ_BY_THE_PAGE_ALONE = new Set<string>([ARCHETYPE_FLAG.Range, ARCHETYPE_FLAG.ExternalSystem, ARCHETYPE_FLAG.MessageKind]);

  it('reads the same marks the orchestrator does', () => {
    const phloem = readFileSync(
      join(here, '..', '..', '..', 'vos.Service.Phloem', 'Model', 'PipelineArchetypes.cs'),
      'utf-8',
    );
    for (const flag of Object.values(ARCHETYPE_FLAG))
      if (!READ_BY_THE_PAGE_ALONE.has(flag)) expect(phloem).toContain(`"${flag}"`);
  });

  /** The predicate marks the page reads that the orchestrator does not: the first two are the platform's
   *  own dispatch marks it never walks, and the orchestrator does not yet act on what a system sends, is
   *  told, or where a kind of message arrives. The state-watch mark is the platform's too, but the
   *  orchestrator walks it to the state a row stands for, so it is held to spelling it. */
  const PREDICATES_READ_BY_THE_PAGE_ALONE = new Set<string>([
    PREDICATE_FLAG.Trigger, PREDICATE_FLAG.JudgedThing,
    PREDICATE_FLAG.Sends, PREDICATE_FLAG.Told, PREDICATE_FLAG.ArrivesAt,
  ]);

  it('reads the same predicate marks the orchestrator starts a run from', () => {
    const phloem = readFileSync(
      join(here, '..', '..', '..', 'vos.Service.Phloem', 'Model', 'PipelinePredicates.cs'),
      'utf-8',
    );
    for (const flag of Object.values(PREDICATE_FLAG))
      if (!PREDICATES_READ_BY_THE_PAGE_ALONE.has(flag)) expect(phloem).toContain(`"${flag}"`);
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
    const relationships: VosRelationship[] = [];
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
    relationships.push({ Id: 'rr1', Name: '', SubjectId: run.Id, PredicateId: has.Id, TargetId: nr1.Id, Properties: {} });
    relationships.push({ Id: 'rr2', Name: '', SubjectId: run.Id, PredicateId: has.Id, TargetId: nr2.Id, Properties: {} });

    const model = new PipelineModel(things, relationships);
    expect(model.runStatus(run.Id)).toBe('running');
    expect(model.nodeRunStatuses(run.Id)).toEqual({ 'node-gen': 'succeeded', 'node-ech': 'running' });
    expect(model.runStatus('missing')).toBeUndefined();
    expect(model.nodeRunStatuses('missing')).toEqual({});
  });

  it('separates the aggregate ring status from per-item fan-out progress (#5648)', () => {
    const things: VosThing[] = [];
    const relationships: VosRelationship[] = [];
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
    for (const nodeRun of [agg, i0, i1, i2])
      relationships.push({ Id: `r${++n}`, Name: '', SubjectId: run.Id, PredicateId: has.Id, TargetId: nodeRun.Id, Properties: {} });

    const model = new PipelineModel(things, relationships);
    expect(model.nodeRunStatuses(run.Id)).toEqual({ 'node-score': 'running' });        // ring = aggregate only
    expect(model.nodeRunProgress(run.Id)).toEqual({ 'node-score': { done: 2, total: 3 } }); // 2 of 3 items terminal
  });

  it('lists a pipeline\'s runs newest-first, scoped to that pipeline', () => {
    const things: VosThing[] = [];
    const relationships: VosRelationship[] = [];
    let n = 0;
    const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
      const t = { Id: `t${++n}`, Name: name, Properties: props };
      things.push(t);
      return t;
    };
    const is = T('is'), of = T('of'), runArchetype = T('Execution', marked(ARCHETYPE_FLAG.PipelineRun));
    const pipeA = T('Pipeline A'), pipeB = T('Pipeline B');
    const relationship = (s: string, p: string, t: string) =>
      relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} });

    // Two runs of A (different times) + one run of B — B must not leak into A's history.
    const a1 = T('PipelineRun a1', { status: 'succeeded', startedUtc: '2026-06-23T10:00:00Z' });
    const a2 = T('PipelineRun a2', { status: 'failed', startedUtc: '2026-06-23T12:00:00Z' });
    const b1 = T('PipelineRun b1', { status: 'succeeded', startedUtc: '2026-06-23T11:00:00Z' });
    for (const r of [a1, a2, b1]) relationship(r.Id, is.Id, runArchetype.Id);
    relationship(a1.Id, of.Id, pipeA.Id);
    relationship(a2.Id, of.Id, pipeA.Id);
    relationship(b1.Id, of.Id, pipeB.Id);

    const model = new PipelineModel(things, relationships);
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
    const relationships: VosRelationship[] = [];
    let n = 0;
    const T = (name: string, props: Record<string, unknown> = {}): VosThing => {
      const t = { Id: `t${++n}`, Name: name, Properties: props };
      things.push(t);
      return t;
    };
    const relationship = (s: string, p: string, t: string) =>
      relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} });
    const is = T('is'), has = T('has'), pipelineArchetype = T('Workflow', marked(ARCHETYPE_FLAG.Pipeline)),
      nodeArchetype = T('Step', marked(ARCHETYPE_FLAG.PipelineNode)),
      connectionArchetype = T('Endpoint', marked(ARCHETYPE_FLAG.Connection));
    const conn = T('conn', { Subdomain: 'x' });
    relationship(conn.Id, is.Id, connectionArchetype.Id);
    const node = T('N', { paramBindings: '{"message":"greeting"}' });
    relationship(node.Id, is.Id, nodeArchetype.Id);
    relationship(node.Id, has.Id, conn.Id);
    const pipe = T('P');
    relationship(pipe.Id, is.Id, pipelineArchetype.Id);
    relationship(pipe.Id, has.Id, node.Id);

    const loaded = loadPipeline(pipe.Id, new PipelineModel(things, relationships))!;
    expect(loaded.nodes[0].paramBindings).toEqual({ message: 'greeting' });
  });
});

// A value the page writes for a name the port archetype declares lands in the Thing's override store,
// not among its own properties. Read from own properties alone, a saved start port fell back to the
// Thing's name and to direction `in`, and the wire drawn from it matched nothing (#7331).
describe('a saved port is read from what it states (#7331)', () => {
  const overridden = (values: Record<string, unknown>): VosThing['InheritedOverrides'] => ({
    'arch-port': { SourceId: 'arch-port', SourceName: 'Socket', InheritedAt: '', Properties: values },
  });

  function modelWithASavedPort() {
    const things: VosThing[] = [];
    const relationships: VosRelationship[] = [];
    let n = 0;
    const T = (id: string, name: string, props: Record<string, unknown> = {}, overrides?: VosThing['InheritedOverrides']) =>
      things.push({ Id: id, Name: name, Properties: props, ...(overrides ? { InheritedOverrides: overrides } : {}) });
    const R = (s: string, p: string, t: string) => relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} });
    T('is', 'is'); T('has', 'has');
    T('arch-port', 'Socket', { [ARCHETYPE_FLAG.Port]: true, portName: '', direction: '', type: '', required: false });
    T('arch-node', 'Step', { [ARCHETYPE_FLAG.PipelineNode]: true });
    T('N1', 'Start'); R('N1', 'is', 'arch-node');
    T('P1', 'subject', {}, overridden({ portName: 'subject', direction: 'out', type: 'any', required: false }));
    R('P1', 'is', 'arch-port'); R('N1', 'has', 'P1');
    return new PipelineModel(things, relationships);
  }

  it('reads a port name and direction stated in the override store', () => {
    const port = modelWithASavedPort().boundaryPortRelationships('N1')[0].port;
    expect(port.portName).toBe('subject');
    expect(port.direction).toBe('out');
    expect(port.type).toBe('any');
  });

  it('states nothing for a Thing the model does not hold', () => {
    expect(modelWithASavedPort().stated('nobody')).toEqual({});
  });

  it('does not hand a member its archetype\'s mark by reading up the chain', () => {
    const model = modelWithASavedPort();
    expect(model.archetypeCarrying(ARCHETYPE_FLAG.Port)).toBe('arch-port');
    expect(model.thingsOfArchetypeCarrying(ARCHETYPE_FLAG.Port).map((t) => t.Id)).toEqual(['P1']);
  });
});
