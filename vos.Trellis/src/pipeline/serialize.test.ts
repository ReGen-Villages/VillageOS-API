import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../api/modelApi', () => ({ modelApi: { applyFragment: vi.fn().mockResolvedValue({}) } }));
vi.mock('../api/thingApi', () => ({
  thingApi: {
    create: vi.fn().mockResolvedValue({ Id: 'new-wire' }),
    addProperty: vi.fn().mockResolvedValue({}),
    setProperty: vi.fn().mockResolvedValue({}),
    remove: vi.fn().mockResolvedValue({}),
  },
}));
vi.mock('../api/relationshipApi', () => ({
  relationshipApi: {
    create: vi.fn().mockResolvedValue({ Id: 'new-rel' }),
    setProperty: vi.fn().mockResolvedValue({}),
    remove: vi.fn().mockResolvedValue({}),
  },
}));

import type { VosThing, VosRelationship } from '../types/vos';
import { PipelineModel, ARCHETYPE_FLAG } from './model';
import { savePipeline, loadPipeline, type EditorNode } from './serialize';
import { modelApi } from '../api/modelApi';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';

const applyFragment = vi.mocked(modelApi.applyFragment);
const thingRemove = vi.mocked(thingApi.remove);
const thingCreate = vi.mocked(thingApi.create);
const thingAddProperty = vi.mocked(thingApi.addProperty);
const thingSetProperty = vi.mocked(thingApi.setProperty);
const relRemove = vi.mocked(relationshipApi.remove);
const relCreate = vi.mocked(relationshipApi.create);
const relSetProperty = vi.mocked(relationshipApi.setProperty);

/** An archetype's own mark, which is the only thing that says what role it plays. */
const marked = (roleFlag: string): Record<string, unknown> => ({ [roleFlag]: true });

// The starting graph every fixture below builds on: the built-in predicates, and each archetype named as
// some model chose and marked as what it is. A fixture that resolves at all therefore proves nothing here
// is found by name (#6530). `T` and `R` add Things and relationships to it.
function graphWithVocabulary() {
  const things: VosThing[] = [];
  const rels: VosRelationship[] = [];
  let n = 0;
  const T = (id: string, name: string, props: Record<string, unknown> = {}) => {
    things.push({ Id: id, Name: name, Properties: props });
  };
  const R = (s: string, p: string, t: string, props: Record<string, unknown> = {}) =>
    rels.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: props });

  T('is', 'is'); T('has', 'has'); T('carries', 'carries');
  T('arch-pipeline', 'Workflow', marked(ARCHETYPE_FLAG.Pipeline));
  T('arch-node', 'Step', marked(ARCHETYPE_FLAG.PipelineNode));
  T('arch-connection', 'Endpoint', marked(ARCHETYPE_FLAG.Connection));
  T('arch-service', 'Capability', marked(ARCHETYPE_FLAG.Service));
  T('arch-port', 'Socket', marked(ARCHETYPE_FLAG.Port));
  T('arch-wire', 'Link', marked(ARCHETYPE_FLAG.PipelineWire));
  // Boundary-node archetypes (#5873); each is-a pipeline node so the node collection picks its instances up.
  T('arch-input', 'Start', marked(ARCHETYPE_FLAG.PipelineInput)); R('arch-input', 'is', 'arch-node');
  T('arch-output', 'Finish', marked(ARCHETYPE_FLAG.PipelineOutput)); R('arch-output', 'is', 'arch-node');
  R('carries', 'is', 'arch-wire');

  return { T, R, things, rels };
}

// One connection, and an existing pipeline P with two nodes N1,N2 and a wire N1.out -> N2.in — enough to
// exercise the in-place-update diff.
function buildModel(): PipelineModel {
  const { T, R, things, rels } = graphWithVocabulary();

  T('svc', 'svc'); R('svc', 'is', 'arch-service');
  T('conn', 'conn', { Subdomain: 'echo' }); R('conn', 'is', 'arch-connection'); R('conn', 'has', 'svc');

  T('P', 'MyPipeline'); R('P', 'is', 'arch-pipeline');
  T('N1', 'Node1'); R('N1', 'is', 'arch-node'); R('N1', 'has', 'conn'); R('P', 'has', 'N1');
  T('N2', 'Node2'); R('N2', 'is', 'arch-node'); R('N2', 'has', 'conn'); R('P', 'has', 'N2');
  R('N1', 'carries', 'N2', { fromPort: 'out', toPort: 'in' });

  return new PipelineModel(things, rels);
}

// The same pipeline, wired by Things instead of edges, and wired twice between one pair — the case an
// edge cannot express, because the model refuses a second edge on one subject, predicate and target.
function buildModelWithHeldWires(): PipelineModel {
  const { T, R, things, rels } = graphWithVocabulary();

  T('svc', 'svc'); R('svc', 'is', 'arch-service');
  T('conn', 'conn', { Subdomain: 'echo' }); R('conn', 'is', 'arch-connection'); R('conn', 'has', 'svc');

  T('P', 'MyPipeline'); R('P', 'is', 'arch-pipeline');
  T('N1', 'Node1'); R('N1', 'is', 'arch-node'); R('N1', 'has', 'conn'); R('P', 'has', 'N1');
  T('N2', 'Node2'); R('N2', 'is', 'arch-node'); R('N2', 'has', 'conn'); R('P', 'has', 'N2');

  T('W1', 'w.out.in', { fromPort: 'out', toPort: 'in', fromPath: '', toPath: '', transform: '' });
  R('W1', 'is', 'arch-wire'); R('N1', 'has', 'W1'); R('W1', 'carries', 'N2');
  T('W2', 'w.trace.context', { fromPort: 'trace', toPort: 'context', fromPath: '', toPath: '', transform: '' });
  R('W2', 'is', 'arch-wire'); R('N1', 'has', 'W2'); R('W2', 'carries', 'N2');

  return new PipelineModel(things, rels);
}

const node = (id: string, label: string, connectionId = 'conn'): EditorNode =>
  ({ id, label, connectionId, x: 10, y: 20, ports: [] });

const wire = (fromPort: string, toPort: string) =>
  ({ id: `e-${fromPort}`, source: 'N1', sourceHandle: fromPort, target: 'N2', targetHandle: toPort });

beforeEach(() => vi.clearAllMocks());

describe('savePipeline — create (no existing pipeline id)', () => {
  it('upserts a new pipeline via one fragment and deletes nothing', async () => {
    const model = buildModel();
    const saved = await savePipeline('Fresh', [node('n1', 'A')], [], model);

    expect(applyFragment).toHaveBeenCalledOnce();
    const frag = JSON.parse(applyFragment.mock.calls[0][0]);
    expect(frag.Things.some((t: { Name: string }) => t.Name === 'Fresh')).toBe(true);
    expect(frag.Things.some((t: { Name: string }) => t.Name === 'A')).toBe(true);
    expect(thingRemove).not.toHaveBeenCalled();
    // The new pipeline got a fresh id (not one of the fixture ids).
    expect(saved.pipelineId).not.toBe('P');
    // …and `is` the archetype this model marks as its pipeline, whatever that archetype is called.
    expect(frag.Relationships.some((r: { Subject: string; Name: string; Target: string }) =>
      r.Subject === saved.pipelineId && r.Name === 'is' && r.Target === 'arch-pipeline')).toBe(true);
  });

  it('refuses a model that marks no archetype, saying so rather than writing a pipeline nothing can find', async () => {
    const model = new PipelineModel(
      [{ Id: 'is', Name: 'is', Properties: {} }, { Id: 'has', Name: 'has', Properties: {} }],
      [],
    );
    await expect(savePipeline('Fresh', [node('n1', 'A')], [], model)).rejects.toThrow(/marks no archetype/);
    expect(applyFragment).not.toHaveBeenCalled();
  });
});

describe('savePipeline — update in place (existing pipeline id)', () => {
  it('reuses the pipeline id and keeps an existing node’s Thing id (no duplicate)', async () => {
    const model = buildModel();
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
    const model = buildModel();
    await savePipeline('MyPipeline', [node('N1', 'Node1')], [], model, 'P');
    expect(thingRemove).toHaveBeenCalledWith('N2');
  });

  it('removes a wire that is gone and creates a wire that is new', async () => {
    const model = buildModel();
    // Drop the N1->N2 wire (no edges) — the one persisted wire should be removed, and no others.
    await savePipeline('MyPipeline', [node('N1', 'Node1'), node('N2', 'Node2')], [], model, 'P');
    expect(relRemove).toHaveBeenCalledTimes(1);
  });

  it('creates a new wire as a Thing with its fromPort/toPort', async () => {
    const model = buildModel();
    const edges = [{ id: 'e1', source: 'N2', sourceHandle: 'out', target: 'N1', targetHandle: 'in' }];
    await savePipeline('MyPipeline', [node('N1', 'Node1'), node('N2', 'Node2')], edges, model, 'P');
    // N2->N1 is new (fixture only had N1->N2). A new wire is written held, whatever shape the wire it
    // sits beside is in — held is the only shape that lets a node pair carry more than one.
    expect(thingCreate).toHaveBeenCalled();
    expect(thingAddProperty).toHaveBeenCalledWith('new-wire', 'fromPort', 'vos.String', 'out');
    expect(relCreate).toHaveBeenCalledWith('N2', 'has', 'new-wire');
    expect(relCreate).toHaveBeenCalledWith('new-wire', 'carries', 'N1');
  });
});

// Boundary I/O nodes (#5873) --------------------------------------------------------------------------

type Frag = {
  Things: { Id: string; Name: string; Properties: Record<string, unknown> }[];
  Relationships: { Name: string; Subject: string; Predicate: string; Target: string }[];
};

describe('savePipeline — boundary nodes (#5873)', () => {
  it('persists an Input node under the archetypes marked as input and as node, with its declared output port and no connection', async () => {
    const model = buildModel();
    const inputNode: EditorNode = {
      id: 'nin', label: 'Input', connectionId: '', x: 0, y: 0, kind: 'input',
      ports: [{ portName: 'seed', direction: 'out', type: 'any', required: false }],
    };

    await savePipeline('B', [inputNode], [], model);

    const frag: Frag = JSON.parse(applyFragment.mock.calls[0][0]);
    const nodeThing = frag.Things.find((t) => t.Name === 'Input')!;
    const isTargets = frag.Relationships.filter((r) => r.Subject === nodeThing.Id && r.Name === 'is').map((r) => r.Target);
    expect(isTargets).toContain('arch-input');
    expect(isTargets).toContain('arch-node');

    // Declares a port child (has → the marked port archetype, direction out) …
    const portThing = frag.Things.find((t) => t.Name === 'seed')!;
    expect(portThing).toBeTruthy();
    expect(frag.Relationships.some((r) => r.Subject === portThing.Id && r.Name === 'is' && r.Target === 'arch-port')).toBe(true);
    expect(frag.Relationships.some((r) => r.Subject === nodeThing.Id && r.Name === 'has' && r.Target === portThing.Id)).toBe(true);
    // … and binds NO connection.
    expect(frag.Relationships.some((r) => r.Subject === nodeThing.Id && r.Name === 'has' && r.Target === 'conn')).toBe(false);
  });
});

describe('loadPipeline — boundary nodes (#5873)', () => {
  it('reconstructs a boundary node kind and its declared ports', () => {
    // A pipeline BP → an Output boundary node OUT that declares an input port `result`.
    const { T, R, things, rels } = graphWithVocabulary();

    T('BP', 'Boundary'); R('BP', 'is', 'arch-pipeline');
    T('OUT', 'Output'); R('OUT', 'is', 'arch-output'); R('BP', 'has', 'OUT');
    T('OUT.result', 'result', { portName: 'result', direction: 'in', type: 'any', required: 'true' });
    R('OUT.result', 'is', 'arch-port'); R('OUT', 'has', 'OUT.result');

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
    const model = buildModel();
    const edges = [{ id: 'e', source: 'na', sourceHandle: 'out', target: 'nb', targetHandle: 'in', fromPath: 'user.id', toPath: 'a' }];
    await savePipeline('FM', [node('na', 'A'), node('nb', 'B')], edges, model);
    expect(thingAddProperty).toHaveBeenCalledWith('new-wire', 'fromPath', 'vos.String', 'user.id');
    expect(thingAddProperty).toHaveBeenCalledWith('new-wire', 'toPath', 'vos.String', 'a');
  });

  it('declares an empty field-path rather than leaving it out, so adding one later is an update', async () => {
    const model = buildModel();
    const edges = [{ id: 'e', source: 'na', sourceHandle: 'out', target: 'nb', targetHandle: 'in' }];
    await savePipeline('FM', [node('na', 'A'), node('nb', 'B')], edges, model);
    // A Thing's property write updates and does not create, so a path left undeclared could not be added
    // later without the write answering that there is no such property.
    expect(thingAddProperty).toHaveBeenCalledWith('new-wire', 'fromPath', 'vos.String', '');
    expect(thingAddProperty).toHaveBeenCalledWith('new-wire', 'toPath', 'vos.String', '');
  });
});

describe('loadPipeline — wire field-paths (#5874)', () => {
  it('reconstructs a wire’s fromPath and toPath', () => {
    const { T, R, things, rels } = graphWithVocabulary();

    T('FP', 'FieldPipe'); R('FP', 'is', 'arch-pipeline');
    T('N1', 'N1'); R('N1', 'is', 'arch-node'); R('FP', 'has', 'N1');
    T('N2', 'N2'); R('N2', 'is', 'arch-node'); R('FP', 'has', 'N2');
    R('N1', 'carries', 'N2', { fromPort: 'out', toPort: 'in', fromPath: 'user.id', toPath: 'a' });

    const loaded = loadPipeline('FP', new PipelineModel(things, rels))!;
    const edge = loaded.edges[0];
    expect(edge.fromPath).toBe('user.id');
    expect(edge.toPath).toBe('a');
  });
});

// On-wire JSONata transforms (#5875) ------------------------------------------------------------------

describe('savePipeline / loadPipeline — wire transform (#5875)', () => {
  it('persists a new wire’s transform', async () => {
    const model = buildModel();
    const edges = [{ id: 'e', source: 'na', sourceHandle: 'out', target: 'nb', targetHandle: 'in', transform: '{"name": firstName}' }];
    await savePipeline('T', [node('na', 'A'), node('nb', 'B')], edges, model);
    expect(thingAddProperty).toHaveBeenCalledWith('new-wire', 'transform', 'vos.String', '{"name": firstName}');
  });

  it('reconstructs a wire’s transform on load', () => {
    const { T, R, things, rels } = graphWithVocabulary();

    T('TP', 'TransformPipe'); R('TP', 'is', 'arch-pipeline');
    T('N1', 'N1'); R('N1', 'is', 'arch-node'); R('TP', 'has', 'N1');
    T('N2', 'N2'); R('N2', 'is', 'arch-node'); R('TP', 'has', 'N2');
    R('N1', 'carries', 'N2', { fromPort: 'out', toPort: 'in', transform: '{"x": y}' });

    const loaded = loadPipeline('TP', new PipelineModel(things, rels))!;
    expect(loaded.edges[0].transform).toBe('{"x": y}');
  });
});

describe('a wire held as a Thing', () => {
  it('draws every wire a node holds, including two between one pair', () => {
    const loaded = loadPipeline('P', buildModelWithHeldWires())!;

    expect(loaded.edges.map((e) => [e.source, e.sourceHandle, e.target, e.targetHandle])).toEqual([
      ['N1', 'out', 'N2', 'in'],
      ['N1', 'trace', 'N2', 'context'],
    ]);
  });

  it('saves a new wire as a Thing the source node holds, pointing at its target', async () => {
    // A third wire between the same pair, beside the two the fixture already holds.
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')],
      [wire('out', 'in'), wire('trace', 'context'), wire('extra', 'spare')],
      buildModelWithHeldWires(), 'P');

    expect(relCreate.mock.calls).toEqual(
      expect.arrayContaining([
        ['new-wire', 'is', 'arch-wire'],
        ['N1', 'has', 'new-wire'],
        ['new-wire', 'carries', 'N2'],
      ]),
    );
  });

  it('declares every port and path on a new wire, empty where unset, so a later edit is an update', async () => {
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')],
      [wire('out', 'in'), wire('trace', 'context'), wire('extra', 'spare')],
      buildModelWithHeldWires(), 'P');

    const declared = Object.fromEntries(thingAddProperty.mock.calls.map((c) => [c[1], c[3]]));
    expect(declared).toEqual({ fromPort: 'extra', toPort: 'spare', fromPath: '', toPath: '', transform: '' });
  });

  it('skips a wire that points at nothing, because an editor writes one a piece at a time', () => {
    const { T, R, things, rels } = graphWithVocabulary();
    T('P', 'MyPipeline'); R('P', 'is', 'arch-pipeline');
    T('N1', 'Node1'); R('N1', 'is', 'arch-node'); R('P', 'has', 'N1');
    T('N2', 'Node2'); R('N2', 'is', 'arch-node'); R('P', 'has', 'N2');
    T('W1', 'w.out.in', { fromPort: 'out', toPort: 'in' });
    R('W1', 'is', 'arch-wire'); R('N1', 'has', 'W1'); R('W1', 'carries', 'N2');
    // Held, of the wire archetype, and pointing at nothing at all.
    T('W-half', 'w.half', { fromPort: 'x', toPort: 'y' });
    R('W-half', 'is', 'arch-wire'); R('N1', 'has', 'W-half');

    const loaded = loadPipeline('P', new PipelineModel(things, rels))!;

    expect(loaded.edges.map((e) => e.sourceHandle)).toEqual(['out']);
  });

  it('skips a wire whose Thing has gone, which is the state a removal leaves behind', () => {
    // Removing a wire retracts its Thing and leaves its `is`, `has` and pointing edges: the platform's
    // delete does not cascade. The Thing goes from the model and the edges do not, so the reader meets a
    // `has` edge whose target it cannot find — and must not draw a wire that was removed.
    const { T, R, things, rels } = graphWithVocabulary();
    T('P', 'MyPipeline'); R('P', 'is', 'arch-pipeline');
    T('N1', 'Node1'); R('N1', 'is', 'arch-node'); R('P', 'has', 'N1');
    T('N2', 'Node2'); R('N2', 'is', 'arch-node'); R('P', 'has', 'N2');
    T('W1', 'w.out.in', { fromPort: 'out', toPort: 'in' });
    R('W1', 'is', 'arch-wire'); R('N1', 'has', 'W1'); R('W1', 'carries', 'N2');
    // The edges of a wire whose Thing is no longer in the model.
    R('N1', 'has', 'W-gone'); R('W-gone', 'carries', 'N2');

    const loaded = loadPipeline('P', new PipelineModel(things, rels))!;

    expect(loaded.edges.map((e) => e.sourceHandle)).toEqual(['out']);
  });

  it('leaves two wires between one node pair alone when neither changed', async () => {
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')],
      [wire('out', 'in'), wire('trace', 'context')], buildModelWithHeldWires(), 'P');

    expect(thingCreate).not.toHaveBeenCalled();
    expect(thingRemove).not.toHaveBeenCalled();
    expect(relRemove).not.toHaveBeenCalled();
  });

  it('removes a held wire by removing its Thing, not a relationship', async () => {
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')], [wire('out', 'in')],
      buildModelWithHeldWires(), 'P');

    expect(thingRemove).toHaveBeenCalledWith('W2');
    expect(relRemove).not.toHaveBeenCalled();
  });

  it('re-writes a changed field-path on the wire Thing, not on an edge', async () => {
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')],
      [{ ...wire('out', 'in'), fromPath: 'body.id' }, wire('trace', 'context')],
      buildModelWithHeldWires(), 'P');

    expect(thingSetProperty).toHaveBeenCalledWith('W1', 'fromPath', 'vos.String', 'body.id');
    expect(relSetProperty).not.toHaveBeenCalled();
  });
});
