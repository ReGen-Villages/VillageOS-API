/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync, readdirSync } from 'node:fs';
import { basename, join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { VosThing, VosRelationship } from '../types/vos';
import { PipelineModel, ROLE, typesCompatible } from './model';
import { loadPipeline } from './serialize';
import { playing, withRoles } from './roles.test.fixture';

// Build the demo model (Generate –carries(echo→message)→ Echo), node -has-> connection[subdomain] -has-> service.
// Every archetype here is named something the editor has never heard of and says what it is through the role
// Thing that binds it, so a fixture that resolves at all proves nothing is found by name.
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
  const pipelineA = T('Workflow', playing(ROLE.Pipeline)),
    nodeA = T('Step', playing(ROLE.PipelineNode)),
    connA = T('Endpoint', playing(ROLE.Connection)),
    serviceA = T('Capability', playing(ROLE.Service)),
    portA = T('Socket', playing(ROLE.Port)),
    wireA = T('Link', playing(ROLE.PipelineWire));
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

  return { model: new PipelineModel(...withRoles(things, relationships)), pipelineId: pipe.Id, pipelineArchetypeId: pipelineA.Id };
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
  const nodeA = T('Step', playing(ROLE.PipelineNode));
  const wireA = T('Link', playing(ROLE.PipelineWire));

  const wireOne = T('w.echo', { fromPort: 'echo', toPort: 'message' });
  const wireTwo = T('w.trace', { fromPort: 'trace', toPort: 'context' });
  const carries = T('carries');
  // Both shapes are read while one replaces the other: this model holds three Things of the wire archetype
  // and only one of them is the predicate.
  R(carries.Id, is.Id, wireA.Id);

  const gen = T('Generate'), ech = T('Echo');
  R(gen.Id, is.Id, nodeA.Id);
  R(ech.Id, is.Id, nodeA.Id);

  for (const wire of [wireOne, wireTwo]) {
    R(wire.Id, is.Id, wireA.Id);
    R(gen.Id, has.Id, wire.Id);
    R(wire.Id, carries.Id, ech.Id);
  }

  return { model: new PipelineModel(...withRoles(things, relationships)), carriesId: carries.Id };
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

  it('identifies the wire predicate by the role its archetype is bound to, not by name', () => {
    const { model } = demoModel();
    expect(model.wirePredicateId()).toBe(model.predicateIdByName('carries'));
  });

  it('does not mistake a wire held as a Thing for the wire predicate, whichever the model lists first', () => {
    const { model, carriesId } = heldWireModel();
    expect(model.wirePredicateId()).toBe(carriesId);
  });

  it('gives the archetype a save writes its `is` edge to, whatever the model calls it', () => {
    const { model, pipelineArchetypeId } = demoModel();
    expect(model.boundTo(ROLE.Pipeline)).toBe(pipelineArchetypeId);
    expect(model.boundTo('a role nothing binds')).toBeUndefined();
  });

  it('takes neither Thing a role binds when it binds two, rather than whichever the model lists first', () => {
    const is: VosThing = { Id: 'is', Name: 'is', Properties: {} };
    const workflow: VosThing = { Id: 'workflow', Name: 'Workflow', Properties: playing(ROLE.Pipeline) };
    const routine: VosThing = { Id: 'routine', Name: 'Routine', Properties: playing(ROLE.Pipeline) };
    const digest: VosThing = { Id: 'digest', Name: 'Digest', Properties: {} };
    const model = new PipelineModel(...withRoles([is, workflow, routine, digest],
      [{ Id: 'digest is', Name: '', SubjectId: digest.Id, PredicateId: is.Id, TargetId: workflow.Id, Properties: {} }]));

    expect(model.boundTo(ROLE.Pipeline)).toBeUndefined();
    expect(model.isOf(digest.Id, ROLE.Pipeline)).toBe(false);
  });

  it('follows only a predicate named exactly as asked, case included', () => {
    const is: VosThing = { Id: 'is', Name: 'is', Properties: {} };
    const recased: VosThing = { Id: 'Has', Name: 'Has', Properties: {} };
    const pipeline: VosThing = { Id: 'pipeline', Name: 'Harvest', Properties: {} };
    const node: VosThing = { Id: 'node', Name: 'Rain gauge', Properties: {} };
    const model = new PipelineModel(...withRoles([is, recased, pipeline, node],
      [{ Id: 'pipeline Has node', Name: '', SubjectId: pipeline.Id, PredicateId: recased.Id, TargetId: node.Id, Properties: {} }]));

    expect(model.outgoing(pipeline.Id, 'has')).toEqual([]);
    expect(model.predicateIdByName('has')).toBeUndefined();
  });

  it('counts a wire Thing as held only along a predicate named exactly "has"', () => {
    const is: VosThing = { Id: 'is', Name: 'is', Properties: {} };
    const recased: VosThing = { Id: 'Has', Name: 'Has', Properties: {} };
    const wireArchetype: VosThing = { Id: 'wire', Name: 'Channel', Properties: playing(ROLE.PipelineWire) };
    const first: VosThing = { Id: 'first', Name: 'First channel', Properties: {} };
    const second: VosThing = { Id: 'second', Name: 'Second channel', Properties: {} };
    const node: VosThing = { Id: 'node', Name: 'Rain gauge', Properties: {} };
    const model = new PipelineModel(...withRoles([is, recased, wireArchetype, first, second, node], [
      { Id: 'first is', Name: '', SubjectId: first.Id, PredicateId: is.Id, TargetId: wireArchetype.Id, Properties: {} },
      { Id: 'second is', Name: '', SubjectId: second.Id, PredicateId: is.Id, TargetId: wireArchetype.Id, Properties: {} },
      { Id: 'node Has first', Name: '', SubjectId: node.Id, PredicateId: recased.Id, TargetId: first.Id, Properties: {} },
    ]));

    expect(model.wirePredicateId()).toBe(first.Id);
  });

  it('does not count an archetype as playing its own role, so the picker lists pipelines and not the archetype', () => {
    const { model, pipelineId, pipelineArchetypeId } = demoModel();
    expect(model.isOf(pipelineId, ROLE.Pipeline)).toBe(true);
    expect(model.isOf(pipelineArchetypeId, ROLE.Pipeline)).toBe(false);
  });

  it('reaches the bound ancestor by either route, and gives up on an `is` cycle instead of walking forever', () => {
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

    // A node under two archetypes that share one bound ancestor: it is reached twice, answered once.
    const root = T('Step', playing(ROLE.PipelineNode)), left = T('Left'), right = T('Right');
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

    const model = new PipelineModel(...withRoles(things, relationships));
    expect(model.isOf(node.Id, ROLE.PipelineNode)).toBe(true);
    expect(model.isOf(inLoop.Id, ROLE.PipelineNode)).toBe(false);
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

  /** The roles the page reads that the orchestrator does not: the range, trigger and judged-Thing roles are
   *  the platform's own and the orchestrator never walks them, it does not yet act on a kind of message or on
   *  what a system sends, is told, or where a kind of message arrives, and its own prototype's role is how the
   *  page finds the service to bind a state to. */
  const READ_BY_THE_PAGE_ALONE = new Set<string>([
    ROLE.Range, ROLE.MessageKind, ROLE.Orchestrator,
    ROLE.Trigger, ROLE.JudgedThing, ROLE.Sends, ROLE.Told, ROLE.ArrivesAt,
  ]);

  it('reads every shared role under the identifier the orchestrator reads it by', () => {
    const phloem = readFileSync(
      join(here, '..', '..', '..', 'vos.Service.Phloem', 'Model', 'PipelineRoles.cs'),
      'utf-8',
    );
    for (const roleId of Object.values(ROLE))
      if (!READ_BY_THE_PAGE_ALONE.has(roleId)) expect(phloem).toContain(`"${roleId}"`);
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

describe('run animation source', () => {
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
    const run = T('PipelineRun abc', { status: 'running' });
    const nr1 = T('NodeRun Generate', { nodeId: 'node-gen', status: 'succeeded' });
    const nr2 = T('NodeRun Echo', { nodeId: 'node-ech', status: 'running' });
    relationships.push({ Id: 'rr1', Name: '', SubjectId: run.Id, PredicateId: has.Id, TargetId: nr1.Id, Properties: {} });
    relationships.push({ Id: 'rr2', Name: '', SubjectId: run.Id, PredicateId: has.Id, TargetId: nr2.Id, Properties: {} });

    const model = new PipelineModel(...withRoles(things, relationships));
    expect(model.runStatus(run.Id)).toBe('running');
    expect(model.nodeRunStatuses(run.Id)).toEqual({ 'node-gen': 'succeeded', 'node-ech': 'running' });
    expect(model.runStatus('missing')).toBeUndefined();
    expect(model.nodeRunStatuses('missing')).toEqual({});
  });

  it('separates the aggregate ring status from per-item fan-out progress', () => {
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

    const model = new PipelineModel(...withRoles(things, relationships));
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
    const is = T('is'), of = T('of'), runArchetype = T('Execution', playing(ROLE.PipelineRun));
    const pipeA = T('Pipeline A'), pipeB = T('Pipeline B');
    const relationship = (s: string, p: string, t: string) =>
      relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} });

    // Two runs of A (different times) + one run of B — B must not leak into A's history.
    const a1 = T('PipelineRun a1', { status: 'succeeded', startedUtc: '2026-06-23T10:00:00Z' });
    const a2 = T('PipelineRun a2', { status: 'failed', startedUtc: '2026-06-23T12:00:00Z', requestId: '01999a2b-0000-7000-8000-000000000001' });
    const b1 = T('PipelineRun b1', { status: 'succeeded', startedUtc: '2026-06-23T11:00:00Z' });
    for (const r of [a1, a2, b1]) relationship(r.Id, is.Id, runArchetype.Id);
    relationship(a1.Id, of.Id, pipeA.Id);
    relationship(a2.Id, of.Id, pipeA.Id);
    relationship(b1.Id, of.Id, pipeB.Id);

    const model = new PipelineModel(...withRoles(things, relationships));
    const runs = model.runsOf(pipeA.Id);
    expect(runs.map((r) => r.runId)).toEqual([a2.Id, a1.Id]); // newest first, B excluded
    expect(runs[0]).toMatchObject({ status: 'failed', startedUtc: '2026-06-23T12:00:00Z' });
    expect(runs[0].requestId).toBe('01999a2b-0000-7000-8000-000000000001');
    expect(runs[1].requestId).toBeUndefined();
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

  it('parses a node\'s parameterBindings property', () => {
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
    const is = T('is'), has = T('has'), pipelineArchetype = T('Workflow', playing(ROLE.Pipeline)),
      nodeArchetype = T('Step', playing(ROLE.PipelineNode)),
      connectionArchetype = T('Endpoint', playing(ROLE.Connection));
    const conn = T('conn', { Subdomain: 'x' });
    relationship(conn.Id, is.Id, connectionArchetype.Id);
    const node = T('N', { parameterBindings: '{"message":"greeting"}' });
    relationship(node.Id, is.Id, nodeArchetype.Id);
    relationship(node.Id, has.Id, conn.Id);
    const pipe = T('P');
    relationship(pipe.Id, is.Id, pipelineArchetype.Id);
    relationship(pipe.Id, has.Id, node.Id);

    const loaded = loadPipeline(pipe.Id, new PipelineModel(...withRoles(things, relationships)))!;
    expect(loaded.nodes[0].parameterBindings).toEqual({ message: 'greeting' });
  });
});

// A value the page writes for a name the port archetype declares lands in the Thing's override store,
// not among its own properties. Read from own properties alone, a saved start port fell back to the
// Thing's name and to direction `in`, and the wire drawn from it matched nothing.
describe('a saved port is read from what it states', () => {
  const overridden = (values: Record<string, unknown>): VosThing['InheritedOverrides'] => ({
    'arch-port': { SourceId: 'arch-port', SourceName: 'Socket', Properties: values },
  });

  function modelWithASavedPort() {
    const things: VosThing[] = [];
    const relationships: VosRelationship[] = [];
    let n = 0;
    const T = (id: string, name: string, props: Record<string, unknown> = {}, overrides?: VosThing['InheritedOverrides']) =>
      things.push({ Id: id, Name: name, Properties: props, ...(overrides ? { InheritedOverrides: overrides } : {}) });
    const R = (s: string, p: string, t: string) => relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} });
    T('is', 'is'); T('has', 'has');
    T('arch-port', 'Socket', { ...playing(ROLE.Port), portName: '', direction: '', type: '', required: false });
    T('arch-node', 'Step', playing(ROLE.PipelineNode));
    T('N1', 'Start'); R('N1', 'is', 'arch-node');
    T('P1', 'subject', {}, overridden({ portName: 'subject', direction: 'out', type: 'any', required: false }));
    R('P1', 'is', 'arch-port'); R('N1', 'has', 'P1');
    return new PipelineModel(...withRoles(things, relationships));
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

  it('takes the archetype a role binds as the archetype, never one of its members', () => {
    const model = modelWithASavedPort();
    expect(model.boundTo(ROLE.Port)).toBe('arch-port');
    expect(model.thingsOf(ROLE.Port).map((t) => t.Id)).toEqual(['P1']);
  });
});

// The playground ships its pipelines drawn, so the wire predicate was always in use before it was
// asked for. A model built from templates declares the predicate and draws nothing, and the first
// pipeline anyone drew on it could not be saved.
describe('the wire predicate before any wire is drawn', () => {
  function vocabularyAndNoWire() {
    const things: VosThing[] = [];
    const relationships: VosRelationship[] = [];
    let n = 0;
    const T = (id: string, name: string, props: Record<string, unknown> = {}) => things.push({ Id: id, Name: name, Properties: props });
    const R = (s: string, p: string, t: string) => relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: {} });
    T('is', 'is'); T('has', 'has');
    T('arch-wire', 'Link', playing(ROLE.PipelineWire));
    T('arch-node', 'Step', playing(ROLE.PipelineNode));
    T('carries', 'carries'); R('carries', 'is', 'arch-wire');
    return { things, relationships, T, R };
  }

  it('finds the predicate by its shape when nothing has used it yet', () => {
    const { things, relationships } = vocabularyAndNoWire();
    expect(new PipelineModel(...withRoles(things, relationships)).wirePredicateId()).toBe('carries');
  });

  it('still never mistakes a held wire for the predicate, whichever the model lists first', () => {
    const { things, relationships, T, R } = vocabularyAndNoWire();
    T('N1', 'Node1'); R('N1', 'is', 'arch-node');
    T('N2', 'Node2'); R('N2', 'is', 'arch-node');
    T('W', 'w', { fromPort: 'out', toPort: 'in' }); R('W', 'is', 'arch-wire'); R('N1', 'has', 'W');
    things.reverse();
    expect(new PipelineModel(...withRoles(things, relationships)).wirePredicateId()).toBe('carries');
  });

  it('finds nothing where the model holds only wires and no predicate', () => {
    const { things, relationships, T, R } = vocabularyAndNoWire();
    things.splice(things.findIndex((t) => t.Id === 'carries'), 1);
    relationships.splice(0, relationships.length);
    T('N1', 'Node1'); R('N1', 'is', 'arch-node');
    T('W', 'w'); R('W', 'is', 'arch-wire'); R('N1', 'has', 'W');
    expect(new PipelineModel(...withRoles(things, relationships)).wirePredicateId()).toBeUndefined();
  });
});
