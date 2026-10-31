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
    remove: vi.fn().mockResolvedValue({}),
  },
}));

import type { VosThing, VosRelationship } from '../types/vos';
import { PipelineModel, ROLE } from './model';
import { playing, withRoles } from './roles.test.fixture';
import { savePipeline, loadPipeline, type EditorNode } from './serialize';
import { modelApi } from '../api/modelApi';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';

const applyFragment = vi.mocked(modelApi.applyFragment);
const thingRemove = vi.mocked(thingApi.remove);
const thingCreate = vi.mocked(thingApi.create);
const thingAddProperty = vi.mocked(thingApi.addProperty);
const thingSetProperty = vi.mocked(thingApi.setProperty);
const relationshipRemove = vi.mocked(relationshipApi.remove);
const relationshipCreate = vi.mocked(relationshipApi.create);

// The starting graph every fixture below builds on: the built-in predicates, and each archetype named as
// some model chose and bound to the role it plays. A fixture that resolves at all therefore proves nothing
// here is found by name. `T` and `R` add Things and relationships to it.
function graphWithVocabulary() {
  const things: VosThing[] = [];
  const relationships: VosRelationship[] = [];
  let n = 0;
  const T = (id: string, name: string, props: Record<string, unknown> = {}) => {
    things.push({ Id: id, Name: name, Properties: props });
  };
  const R = (s: string, p: string, t: string, props: Record<string, unknown> = {}) =>
    relationships.push({ Id: `r${++n}`, Name: '', SubjectId: s, PredicateId: p, TargetId: t, Properties: props });

  T('is', 'is'); T('has', 'has'); T('carries', 'carries');
  T('arch-pipeline', 'Workflow', playing(ROLE.Pipeline));
  T('arch-node', 'Step', playing(ROLE.PipelineNode));
  T('arch-connection', 'Endpoint', playing(ROLE.Connection));
  T('arch-service', 'Capability', playing(ROLE.Service));
  T('arch-port', 'Socket', playing(ROLE.Port));
  T('arch-wire', 'Link', playing(ROLE.PipelineWire));
  T('input-connector', 'takesFrom', playing(ROLE.InputConnector));
  T('output-connector', 'deliversTo', playing(ROLE.OutputConnector));
  // Boundary-node archetypes; each is-a pipeline node so the node collection picks its instances up.
  T('arch-input', 'Start', playing(ROLE.PipelineInput)); R('arch-input', 'is', 'arch-node');
  T('arch-output', 'Finish', playing(ROLE.PipelineOutput)); R('arch-output', 'is', 'arch-node');
  R('carries', 'is', 'arch-wire');
  T('taken-on', 'firesOn', playing(ROLE.TakenOn));
  T('success', 'Worked', playing(ROLE.Success));
  T('fail', 'Broke', playing(ROLE.Fail));
  T('arch-choice', 'Branch', playing(ROLE.DecisionPath));

  const port = (id: string, portName: string, direction: 'in' | 'out', owner: string) => {
    T(id, portName, { portName, direction, type: 'any', required: 'false' });
    R(id, 'is', 'arch-port'); R(owner, 'has', id);
  };
  const heldWire = (id: string, from: string, outPort: string, to: string, inPort: string, values: Record<string, unknown> = {}) => {
    T(id, `${outPort} to ${inPort}`, { fromPath: '', toPath: '', transform: '', ...values });
    R(id, 'is', 'arch-wire'); R(from, 'has', id); R(id, 'carries', to);
    R(id, 'input-connector', outPort); R(id, 'output-connector', inPort);
    R(id, 'taken-on', 'success');
  };

  return { T, R, port, heldWire, things, relationships };
}

// One connection whose service has an out port `out` and an in port `in`, and an existing pipeline P with two
// nodes N1,N2 and a wire N1.out -> N2.in — enough to exercise the in-place-update diff.
function buildModel(): PipelineModel {
  const { T, R, port, heldWire, things, relationships } = graphWithVocabulary();

  T('svc', 'svc'); R('svc', 'is', 'arch-service');
  port('svc.out', 'out', 'out', 'svc'); port('svc.in', 'in', 'in', 'svc');
  T('conn', 'conn', { Subdomain: 'echo' }); R('conn', 'is', 'arch-connection'); R('conn', 'has', 'svc');

  T('P', 'MyPipeline'); R('P', 'is', 'arch-pipeline');
  T('N1', 'Node1'); R('N1', 'is', 'arch-node'); R('N1', 'has', 'conn'); R('P', 'has', 'N1');
  T('N2', 'Node2'); R('N2', 'is', 'arch-node'); R('N2', 'has', 'conn'); R('P', 'has', 'N2');
  heldWire('W0', 'N1', 'svc.out', 'N2', 'svc.in');

  return new PipelineModel(...withRoles(things, relationships));
}

// The same pipeline wired twice between one pair, which relationships between the nodes could not carry, because
// the model refuses a second relationship on one subject, predicate and target.
function buildModelWithHeldWires(): PipelineModel {
  const { T, R, port, heldWire, things, relationships } = graphWithVocabulary();

  T('svc', 'svc'); R('svc', 'is', 'arch-service');
  port('svc.out', 'out', 'out', 'svc'); port('svc.in', 'in', 'in', 'svc');
  port('svc.trace', 'trace', 'out', 'svc'); port('svc.context', 'context', 'in', 'svc');
  T('conn', 'conn', { Subdomain: 'echo' }); R('conn', 'is', 'arch-connection'); R('conn', 'has', 'svc');

  T('P', 'MyPipeline'); R('P', 'is', 'arch-pipeline');
  T('N1', 'Node1'); R('N1', 'is', 'arch-node'); R('N1', 'has', 'conn'); R('P', 'has', 'N1');
  T('N2', 'Node2'); R('N2', 'is', 'arch-node'); R('N2', 'has', 'conn'); R('P', 'has', 'N2');
  heldWire('W1', 'N1', 'svc.out', 'N2', 'svc.in');
  heldWire('W2', 'N1', 'svc.trace', 'N2', 'svc.context');

  return new PipelineModel(...withRoles(things, relationships));
}

const node = (id: string, label: string, connectionId = 'conn'): EditorNode =>
  ({ id, label, connectionId, x: 10, y: 20, ports: [] });

const wire = (outPortId: string, inPortId: string) =>
  ({ id: `e-${outPortId}`, source: 'N1', sourceHandle: outPortId, target: 'N2', targetHandle: inPortId });

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
    expect(saved.pipelineId).not.toBe('P');
    // …and `is` the archetype this model binds to the pipeline role, whatever that archetype is called.
    expect(frag.Relationships.some((r: { Subject: string; Name: string; Target: string }) =>
      r.Subject === saved.pipelineId && r.Name === 'is' && r.Target === 'arch-pipeline')).toBe(true);
  });

  it('saves every pipeline published, so the model starts what the page draws', async () => {
    const model = buildModel();
    await savePipeline('Fresh', [node('n1', 'A')], [], model);
    await savePipeline('Kept', [node('n1', 'A')], [], model, 'P');

    for (const call of applyFragment.mock.calls) {
      const frag = JSON.parse(call[0]);
      const pipeline = frag.Things.find((t: { Name: string }) => t.Name === 'Fresh' || t.Name === 'Kept');
      expect(pipeline.Properties.published).toEqual({ typeInfo: 'vos.Boolean', value: true });
    }
  });

  it('persists a node’s parameter bindings under the property spelled in full', async () => {
    const model = buildModel();
    await savePipeline('Fresh', [{ ...node('n1', 'A'), parameterBindings: { message: 'greeting' } }], [], model);

    const frag = JSON.parse(applyFragment.mock.calls[0][0]);
    const a = frag.Things.find((t: { Name: string }) => t.Name === 'A');
    expect(a.Properties.parameterBindings.value).toBe('{"message":"greeting"}');
    expect(Object.keys(a.Properties)).toEqual(['x', 'y', 'parameterBindings']);
  });

  it('refuses a model that binds no archetype, saying so rather than writing a pipeline nothing can find', async () => {
    const model = new PipelineModel(
      [{ Id: 'is', Name: 'is', Properties: {} }, { Id: 'has', Name: 'has', Properties: {} }],
      [],
    );
    await expect(savePipeline('Fresh', [node('n1', 'A')], [], model)).rejects.toThrow(/does not declare: .*pipeline.*node.*wire/);
    expect(applyFragment).not.toHaveBeenCalled();
  });
});

describe('savePipeline — update in place (existing pipeline id)', () => {
  it('reuses the pipeline id and keeps an existing node’s Thing id (no duplicate)', async () => {
    const model = buildModel();
    // Keep only N1 (its canvas id is its Thing id), moved; drop N2.
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

  it('removes a wire that is gone by removing its Thing', async () => {
    const model = buildModel();
    await savePipeline('MyPipeline', [node('N1', 'Node1'), node('N2', 'Node2')], [], model, 'P');
    expect(thingRemove).toHaveBeenCalledWith('W0');
    expect(relationshipRemove).not.toHaveBeenCalled();
  });

  it('creates a new wire as a Thing related to the out port it takes from and the in port it delivers to', async () => {
    const model = buildModel();
    const edges = [{ id: 'e1', source: 'N2', sourceHandle: 'svc.out', target: 'N1', targetHandle: 'svc.in' }];
    await savePipeline('MyPipeline', [node('N1', 'Node1'), node('N2', 'Node2')], edges, model, 'P');
    expect(relationshipCreate).toHaveBeenCalledWith('N2', 'has', 'new-wire');
    expect(relationshipCreate).toHaveBeenCalledWith('new-wire', 'carries', 'N1');
    expect(relationshipCreate).toHaveBeenCalledWith('new-wire', 'input-connector', 'svc.out');
    expect(relationshipCreate).toHaveBeenCalledWith('new-wire', 'output-connector', 'svc.in');
    expect(thingAddProperty.mock.calls.map((call) => call[1])).not.toContain('fromPort');
  });
});

type Frag = {
  Things: { Id: string; Name: string; Properties: Record<string, unknown> }[];
  Relationships: { Name: string; Subject: string; Predicate: string; Target: string }[];
};

describe('savePipeline — boundary nodes', () => {
  it('persists an Input node under the archetypes bound as input and as node, with its declared output port and no connection', async () => {
    const model = buildModel();
    const inputNode: EditorNode = {
      id: 'nin', label: 'Input', connectionId: '', x: 0, y: 0, kind: 'input',
      ports: [{ portId: 'seed-port', portName: 'seed', direction: 'out', type: 'any', required: false }],
    };

    await savePipeline('B', [inputNode], [], model);

    const frag: Frag = JSON.parse(applyFragment.mock.calls[0][0]);
    const nodeThing = frag.Things.find((t) => t.Name === 'Input')!;
    const isTargets = frag.Relationships.filter((r) => r.Subject === nodeThing.Id && r.Name === 'is').map((r) => r.Target);
    expect(isTargets).toContain('arch-input');
    expect(isTargets).toContain('arch-node');

    const portThing = frag.Things.find((t) => t.Name === 'seed')!;
    expect(portThing.Id).toBe('seed-port');
    expect(frag.Relationships.some((r) => r.Subject === portThing.Id && r.Name === 'is' && r.Target === 'arch-port')).toBe(true);
    expect(frag.Relationships.some((r) => r.Subject === nodeThing.Id && r.Name === 'has' && r.Target === portThing.Id)).toBe(true);
    expect(frag.Relationships.some((r) => r.Subject === nodeThing.Id && r.Name === 'has' && r.Target === 'conn')).toBe(false);
  });

  it('renames a boundary node’s port in place, so the wire related to it stays', async () => {
    const { T, R, port, heldWire, things, relationships } = graphWithVocabulary();
    T('BP', 'Boundary'); R('BP', 'is', 'arch-pipeline');
    T('IN', 'Start'); R('IN', 'is', 'arch-input'); R('BP', 'has', 'IN');
    T('OUT', 'Finish'); R('OUT', 'is', 'arch-output'); R('BP', 'has', 'OUT');
    port('IN.subject', 'subject', 'out', 'IN'); port('OUT.result', 'result', 'in', 'OUT');
    heldWire('W', 'IN', 'IN.subject', 'OUT', 'OUT.result');
    const model = new PipelineModel(...withRoles(things, relationships));
    const loaded = loadPipeline('BP', model)!;
    const renamed = loaded.nodes.map((n) => (n.id === 'IN' ? { ...n, ports: n.ports.map((p) => ({ ...p, portName: 'topic' })) } : n));

    await savePipeline(loaded.name, renamed, loaded.edges, model, 'BP');

    const frag: Frag = JSON.parse(applyFragment.mock.calls[0][0]);
    expect(frag.Things.find((t) => t.Id === 'IN.subject')!.Properties.portName).toEqual({ typeInfo: 'vos.String', value: 'topic' });
    expect(thingRemove).not.toHaveBeenCalled();
    expect(thingCreate).not.toHaveBeenCalled();
  });

  it('retracts a port removed from a boundary node and keeps the one still declared', async () => {
    const { T, R, port, things, relationships } = graphWithVocabulary();
    T('BP', 'Boundary'); R('BP', 'is', 'arch-pipeline');
    T('IN', 'Start'); R('IN', 'is', 'arch-input'); R('BP', 'has', 'IN');
    port('IN.subject', 'subject', 'out', 'IN'); port('IN.topic', 'topic', 'out', 'IN');
    const model = new PipelineModel(...withRoles(things, relationships));
    const loaded = loadPipeline('BP', model)!;
    const trimmed = loaded.nodes.map((n) => ({ ...n, ports: n.ports.filter((p) => p.portId !== 'IN.topic') }));

    await savePipeline(loaded.name, trimmed, [], model, 'BP');

    expect(thingRemove.mock.calls.map((call) => call[0])).toEqual(['IN.topic']);
  });
});

describe('loadPipeline — boundary nodes', () => {
  it('reconstructs a boundary node kind and its declared ports', () => {
    // A pipeline BP → an Output boundary node OUT that declares an input port `result`.
    const { T, R, things, relationships } = graphWithVocabulary();

    T('BP', 'Boundary'); R('BP', 'is', 'arch-pipeline');
    T('OUT', 'Output'); R('OUT', 'is', 'arch-output'); R('BP', 'has', 'OUT');
    T('OUT.result', 'result', { portName: 'result', direction: 'in', type: 'any', required: 'true' });
    R('OUT.result', 'is', 'arch-port'); R('OUT', 'has', 'OUT.result');

    const loaded = loadPipeline('BP', new PipelineModel(...withRoles(things, relationships)))!;
    const out = loaded.nodes.find((node) => node.id === 'OUT')!;
    expect(out.kind).toBe('output');
    expect(out.connectionId).toBe('');
    expect(out.ports.map((p) => p.portName)).toContain('result');
    expect(out.ports.find((p) => p.portName === 'result')!.direction).toBe('in');
  });
});

describe('savePipeline — wire field-paths', () => {
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

describe('loadPipeline — wire field-paths', () => {
  it('reconstructs a wire’s fromPath and toPath', () => {
    const { T, R, port, heldWire, things, relationships } = graphWithVocabulary();

    T('FP', 'FieldPipe'); R('FP', 'is', 'arch-pipeline');
    T('N1', 'N1'); R('N1', 'is', 'arch-node'); R('FP', 'has', 'N1');
    T('N2', 'N2'); R('N2', 'is', 'arch-node'); R('FP', 'has', 'N2');
    port('N1.out', 'out', 'out', 'N1'); port('N2.in', 'in', 'in', 'N2');
    heldWire('W', 'N1', 'N1.out', 'N2', 'N2.in', { fromPath: 'user.id', toPath: 'a' });

    const loaded = loadPipeline('FP', new PipelineModel(...withRoles(things, relationships)))!;
    const edge = loaded.edges[0];
    expect(edge.fromPath).toBe('user.id');
    expect(edge.toPath).toBe('a');
  });
});

describe('savePipeline / loadPipeline — wire transform', () => {
  it('persists a new wire’s transform', async () => {
    const model = buildModel();
    const edges = [{ id: 'e', source: 'na', sourceHandle: 'out', target: 'nb', targetHandle: 'in', transform: '{"name": firstName}' }];
    await savePipeline('T', [node('na', 'A'), node('nb', 'B')], edges, model);
    expect(thingAddProperty).toHaveBeenCalledWith('new-wire', 'transform', 'vos.String', '{"name": firstName}');
  });

  it('reconstructs a wire’s transform on load', () => {
    const { T, R, port, heldWire, things, relationships } = graphWithVocabulary();

    T('TP', 'TransformPipe'); R('TP', 'is', 'arch-pipeline');
    T('N1', 'N1'); R('N1', 'is', 'arch-node'); R('TP', 'has', 'N1');
    T('N2', 'N2'); R('N2', 'is', 'arch-node'); R('TP', 'has', 'N2');
    port('N1.out', 'out', 'out', 'N1'); port('N2.in', 'in', 'in', 'N2');
    heldWire('W', 'N1', 'N1.out', 'N2', 'N2.in', { transform: '{"x": y}' });

    const loaded = loadPipeline('TP', new PipelineModel(...withRoles(things, relationships)))!;
    expect(loaded.edges[0].transform).toBe('{"x": y}');
  });
});

describe('a wire held as a Thing', () => {
  it('draws every wire a node holds, including two between one pair', () => {
    const loaded = loadPipeline('P', buildModelWithHeldWires())!;

    expect(loaded.edges.map((e) => [e.source, e.sourceHandle, e.target, e.targetHandle])).toEqual([
      ['N1', 'svc.out', 'N2', 'svc.in'],
      ['N1', 'svc.trace', 'N2', 'svc.context'],
    ]);
  });

  it('saves a new wire as a Thing the source node holds, pointing at its target', async () => {
    // A third wire between the same pair, beside the two the fixture already holds.
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')],
      [wire('svc.out', 'svc.in'), wire('svc.trace', 'svc.context'), wire('svc.out', 'svc.context')],
      buildModelWithHeldWires(), 'P');

    expect(relationshipCreate.mock.calls).toEqual(
      expect.arrayContaining([
        ['new-wire', 'is', 'arch-wire'],
        ['N1', 'has', 'new-wire'],
        ['new-wire', 'carries', 'N2'],
        ['new-wire', 'input-connector', 'svc.out'],
        ['new-wire', 'output-connector', 'svc.context'],
      ]),
    );
  });

  it('declares every path on a new wire, empty where unset, so a later edit is an update', async () => {
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')],
      [wire('svc.out', 'svc.in'), wire('svc.trace', 'svc.context'), wire('svc.out', 'svc.context')],
      buildModelWithHeldWires(), 'P');

    const declared = Object.fromEntries(thingAddProperty.mock.calls.map((c) => [c[1], c[3]]));
    expect(declared).toEqual({ fromPath: '', toPath: '', transform: '' });
  });

  it('skips a wire that points at nothing, or at only one of its ports, because an editor writes one a piece at a time', () => {
    const { T, R, port, heldWire, things, relationships } = graphWithVocabulary();
    T('P', 'MyPipeline'); R('P', 'is', 'arch-pipeline');
    T('N1', 'Node1'); R('N1', 'is', 'arch-node'); R('P', 'has', 'N1');
    T('N2', 'Node2'); R('N2', 'is', 'arch-node'); R('P', 'has', 'N2');
    port('N1.out', 'out', 'out', 'N1'); port('N2.in', 'in', 'in', 'N2');
    heldWire('W1', 'N1', 'N1.out', 'N2', 'N2.in');
    T('W-half', 'w.half'); R('W-half', 'is', 'arch-wire'); R('N1', 'has', 'W-half');
    T('W-one-end', 'w.one-end'); R('W-one-end', 'is', 'arch-wire'); R('N1', 'has', 'W-one-end');
    R('W-one-end', 'carries', 'N2'); R('W-one-end', 'input-connector', 'N1.out');
    T('W-other-end', 'w.other-end'); R('W-other-end', 'is', 'arch-wire'); R('N1', 'has', 'W-other-end');
    R('W-other-end', 'carries', 'N2'); R('W-other-end', 'output-connector', 'N2.in');

    const loaded = loadPipeline('P', new PipelineModel(...withRoles(things, relationships)))!;

    expect(loaded.edges.map((e) => e.id)).toEqual(['W1']);
  });

  it('skips a wire whose Thing has gone, which is the state a removal leaves behind', () => {
    // Removing a wire retracts its Thing and leaves its `is`, `has` and pointing relationships: the platform's
    // delete does not cascade. The Thing goes from the model and the relationships do not, so the reader meets a
    // `has` relationship whose target it cannot find — and must not draw a wire that was removed.
    const { T, R, port, heldWire, things, relationships } = graphWithVocabulary();
    T('P', 'MyPipeline'); R('P', 'is', 'arch-pipeline');
    T('N1', 'Node1'); R('N1', 'is', 'arch-node'); R('P', 'has', 'N1');
    T('N2', 'Node2'); R('N2', 'is', 'arch-node'); R('P', 'has', 'N2');
    port('N1.out', 'out', 'out', 'N1'); port('N2.in', 'in', 'in', 'N2');
    heldWire('W1', 'N1', 'N1.out', 'N2', 'N2.in');
    R('N1', 'has', 'W-gone'); R('W-gone', 'carries', 'N2');

    const loaded = loadPipeline('P', new PipelineModel(...withRoles(things, relationships)))!;

    expect(loaded.edges.map((e) => e.id)).toEqual(['W1']);
  });

  it('leaves two wires between one node pair alone when neither changed', async () => {
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')],
      [wire('svc.out', 'svc.in'), wire('svc.trace', 'svc.context')], buildModelWithHeldWires(), 'P');

    expect(thingCreate).not.toHaveBeenCalled();
    expect(thingRemove).not.toHaveBeenCalled();
    expect(relationshipRemove).not.toHaveBeenCalled();
  });

  it('removes a held wire by removing its Thing, not a relationship', async () => {
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')], [wire('svc.out', 'svc.in')],
      buildModelWithHeldWires(), 'P');

    expect(thingRemove).toHaveBeenCalledWith('W2');
    expect(relationshipRemove).not.toHaveBeenCalled();
  });

  it('re-writes a changed field-path on the wire Thing', async () => {
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')],
      [{ ...wire('svc.out', 'svc.in'), fromPath: 'body.id' }, wire('svc.trace', 'svc.context')],
      buildModelWithHeldWires(), 'P');

    expect(thingSetProperty.mock.calls).toEqual([['W1', 'fromPath', 'vos.String', 'body.id']]);
  });

  it('re-writes a changed to-path and transform on the wire Thing', async () => {
    await savePipeline('P', [node('N1', 'Node1'), node('N2', 'Node2')],
      [wire('svc.out', 'svc.in'), { ...wire('svc.trace', 'svc.context'), toPath: 'trace', transform: '$uppercase($)' }],
      buildModelWithHeldWires(), 'P');

    expect(thingSetProperty.mock.calls).toEqual([
      ['W2', 'toPath', 'vos.String', 'trace'],
      ['W2', 'transform', 'vos.String', '$uppercase($)'],
    ]);
  });
});

describe('savePipeline / loadPipeline — what a boundary node stands for', () => {
  it('reconstructs the Thing a boundary node stands for on load', async () => {
    const { catalystFixture } = await import('./catalysts.test.fixture');
    const { model, id } = catalystFixture();
    const loaded = loadPipeline(id.readingsArrive, model)!;
    expect(loaded.nodes.find((node) => node.id === id.readingsStart)?.standsForId).toBe(id.hourlyReading);
    expect(loaded.nodes.find((node) => node.id === id.readingsEnd)?.standsForId).toBeUndefined();
  });

  it('writes the stands-for relationship through the predicate bound to that role, and retracts the one it replaces', async () => {
    const { catalystFixture } = await import('./catalysts.test.fixture');
    const { model, id } = catalystFixture();
    const loaded = loadPipeline(id.readingsArrive, model)!;
    const nodes = loaded.nodes.map((node) => (node.id === id.readingsStart ? { ...node, standsForId: id.intakeDoor } : node));
    await savePipeline(loaded.name, nodes, loaded.edges, model, id.readingsArrive);

    const fragment = JSON.parse(vi.mocked(modelApi.applyFragment).mock.calls[0][0]);
    const written = fragment.Relationships.filter((relationship: { Predicate: string }) => relationship.Predicate === id.standsFor);
    expect(written).toEqual([{ Name: 'standsFor', Subject: id.readingsStart, Predicate: id.standsFor, Target: id.intakeDoor }]);
    expect(vi.mocked(relationshipApi.remove)).toHaveBeenCalledWith(model.standsFor(id.readingsStart)!.relationshipId);
  });

  it('refuses a node standing for something when the model binds no predicate to that role', async () => {
    const { catalystFixture } = await import('./catalysts.test.fixture');
    const { model, things, relationships, id } = catalystFixture();
    const unbound = relationships.filter((relationship) => relationship.SubjectId !== ROLE.StandsFor);
    const loaded = loadPipeline(id.readingsArrive, model)!;
    await expect(savePipeline(loaded.name, loaded.nodes, loaded.edges, new PipelineModel(things, unbound), id.readingsArrive))
      .rejects.toThrow(/stands for/);
  });
});

describe('loadPipeline — a saved pipeline whose ports are stated in the override store', () => {
  it('returns the wire attached at both ends', () => {
    const { T, R, things, relationships } = graphWithVocabulary();
    const stated = (values: Record<string, unknown>) => ({
      'arch-port': { SourceId: 'arch-port', SourceName: 'Socket', Properties: values },
    });
    T('BP', 'Decided'); R('BP', 'is', 'arch-pipeline');
    T('IN', 'Start'); R('IN', 'is', 'arch-input'); R('BP', 'has', 'IN');
    T('OUT', 'Submitter'); R('OUT', 'is', 'arch-output'); R('BP', 'has', 'OUT');
    things.push({ Id: 'IN.subject', Name: 'subject', Properties: {}, InheritedOverrides: stated({ portName: 'subject', direction: 'out', type: 'any', required: 'false' }) });
    R('IN.subject', 'is', 'arch-port'); R('IN', 'has', 'IN.subject');
    things.push({ Id: 'OUT.payload', Name: 'payload', Properties: {}, InheritedOverrides: stated({ portName: 'payload', direction: 'in', type: 'any', required: 'true' }) });
    R('OUT.payload', 'is', 'arch-port'); R('OUT', 'has', 'OUT.payload');
    T('W', 'subject to payload', { fromPath: '', toPath: '', transform: '' });
    R('W', 'is', 'arch-wire'); R('IN', 'has', 'W'); R('W', 'carries', 'OUT');
    R('W', 'input-connector', 'IN.subject'); R('W', 'output-connector', 'OUT.payload');

    const loaded = loadPipeline('BP', new PipelineModel(...withRoles(things, relationships)))!;

    const start = loaded.nodes.find((node) => node.id === 'IN')!;
    expect(start.ports).toEqual([{ portId: 'IN.subject', portName: 'subject', direction: 'out', type: 'any', required: false }]);
    expect(loaded.edges).toEqual([expect.objectContaining({ source: 'IN', sourceHandle: 'IN.subject', target: 'OUT', targetHandle: 'OUT.payload' })]);
  });
});

// A model built from templates declares the wire predicate and draws nothing. The first pipeline drawn on
// it was refused as declaring no archetype, although it declares every one.
describe('savePipeline — the first pipeline in a model with no wire yet', () => {
  function modelWithNoWire(): PipelineModel {
    const { T, R, things, relationships } = graphWithVocabulary();
    T('svc', 'svc'); R('svc', 'is', 'arch-service');
    T('conn', 'conn', { Subdomain: 'echo' }); R('conn', 'is', 'arch-connection'); R('conn', 'has', 'svc');
    return new PipelineModel(...withRoles(things, relationships));
  }

  it('writes the pipeline and its wire through the predicate the model declares', async () => {
    const model = modelWithNoWire();
    thingCreate.mockResolvedValue({ Id: 'W-new', Name: 'out to in', Properties: {} });

    const saved = await savePipeline('First', [node('N1', 'A'), node('N2', 'B')], [wire('out', 'in')], model);

    expect(applyFragment).toHaveBeenCalledOnce();
    expect(saved.pipelineId).toBeTruthy();
    expect(relationshipCreate).toHaveBeenCalledWith('W-new', 'carries', expect.any(String));
  });

  it('names what it did not find when the model binds no wire archetype', async () => {
    const { T, R, things, relationships } = graphWithVocabulary();
    things.splice(things.findIndex((t) => t.Id === 'arch-wire'), 1);
    T('conn', 'conn', { Subdomain: 'echo' }); R('conn', 'is', 'arch-connection');

    await expect(savePipeline('First', [node('N1', 'A')], [], new PipelineModel(...withRoles(things, relationships))))
      .rejects.toThrow(/wire predicate/);
    expect(applyFragment).not.toHaveBeenCalled();
  });

  it.each([
    ['input-connector', /input connector role/],
    ['output-connector', /output connector role/],
  ])('names the connector it did not find when the model binds no predicate to that role (%s)', async (connector, named) => {
    const { things, relationships } = graphWithVocabulary();
    things.splice(things.findIndex((t) => t.Id === connector), 1);

    await expect(savePipeline('First', [node('N1', 'A')], [], new PipelineModel(...withRoles(things, relationships))))
      .rejects.toThrow(named);
    expect(applyFragment).not.toHaveBeenCalled();
  });
});

describe('savePipeline / loadPipeline — what a wire is taken on', () => {
  it('saves a new wire taken on success when nothing else was chosen', async () => {
    const model = buildModel();
    thingCreate.mockResolvedValueOnce({ Id: 'W-new' } as VosThing);

    await savePipeline('MyPipeline', [node('N1', 'A'), node('N2', 'B')], [wire('svc.out', 'svc.in'), wire('svc.trace', 'svc.in')], model, 'P');

    expect(relationshipCreate).toHaveBeenCalledWith('W-new', 'taken-on', 'success');
  });

  it('saves a new wire taken on what the person chose', async () => {
    const model = buildModel();
    thingCreate.mockResolvedValueOnce({ Id: 'W-new' } as VosThing);

    await savePipeline('MyPipeline', [node('N1', 'A'), node('N2', 'B')],
      [wire('svc.out', 'svc.in'), { ...wire('svc.trace', 'svc.in'), takenOn: ['fail'] }], model, 'P');

    expect(relationshipCreate).toHaveBeenCalledWith('W-new', 'taken-on', 'fail');
    expect(relationshipCreate).not.toHaveBeenCalledWith('W-new', 'taken-on', 'success');
  });

  it('moves an existing wire to the result chosen for it, retracting the one it had', async () => {
    const model = buildModel();
    const [held] = model.outgoingWires('N1');

    await savePipeline('MyPipeline', [node('N1', 'A'), node('N2', 'B')], [{ ...wire('svc.out', 'svc.in'), takenOn: ['fail'] }], model, 'P');

    expect(relationshipCreate).toHaveBeenCalledWith('W0', 'taken-on', 'fail');
    expect(relationshipRemove).toHaveBeenCalledWith(held.takenOn[0].relationshipId);
  });

  it('leaves an existing wire alone when what it is taken on did not change', async () => {
    const model = buildModel();

    await savePipeline('MyPipeline', [node('N1', 'A'), node('N2', 'B')], [{ ...wire('svc.out', 'svc.in'), takenOn: ['success'] }], model, 'P');

    expect(relationshipCreate).not.toHaveBeenCalled();
    expect(relationshipRemove).not.toHaveBeenCalled();
  });

  it('reads back what each wire is taken on', () => {
    const loaded = loadPipeline('P', buildModel())!;

    expect(loaded.edges[0].takenOn).toEqual(['success']);
  });

  it('names what it did not find when the model binds nothing to the taken-on role or to success', async () => {
    for (const missing of ['taken-on', 'success']) {
      const { things, relationships } = graphWithVocabulary();
      things.splice(things.findIndex((t) => t.Id === missing), 1);

      await expect(savePipeline('First', [node('N1', 'A')], [], new PipelineModel(...withRoles(things, relationships))))
        .rejects.toThrow(missing === 'taken-on' ? /taken-on role/ : /success role/);
    }
    expect(applyFragment).not.toHaveBeenCalled();
  });
});

describe('savePipeline / loadPipeline — a node\'s choices and the Output node\'s rule', () => {
  function modelWithAChoice(): PipelineModel {
    const { T, R, port, heldWire, things, relationships } = graphWithVocabulary();
    T('svc', 'svc'); R('svc', 'is', 'arch-service');
    port('svc.out', 'out', 'out', 'svc'); port('svc.in', 'in', 'in', 'svc');
    T('conn', 'conn', { Subdomain: 'echo' }); R('conn', 'is', 'arch-connection'); R('conn', 'has', 'svc');
    T('P', 'MyPipeline'); R('P', 'is', 'arch-pipeline');
    T('N1', 'Node1'); R('N1', 'is', 'arch-node'); R('N1', 'has', 'conn'); R('P', 'has', 'N1');
    T('N2', 'Node2'); R('N2', 'is', 'arch-node'); R('N2', 'has', 'conn'); R('P', 'has', 'N2');
    T('C1', 'Large', { when: 'outputs.out > 10' }); R('C1', 'is', 'arch-choice'); R('N1', 'has', 'C1');
    heldWire('W0', 'N1', 'svc.out', 'N2', 'svc.in');
    T('END', 'Finish', { when: '$count(failures) = 0' }); R('END', 'is', 'arch-output'); R('P', 'has', 'END');
    return new PipelineModel(...withRoles(things, relationships));
  }

  const fragment = () => JSON.parse(applyFragment.mock.calls[0][0]) as {
    Things: Array<{ Id: string; Name: string; Properties: Record<string, { value: unknown }> }>;
    Relationships: Array<{ Subject: string; Predicate: string; Target: string }>;
  };

  it('reads back a node\'s choices and the Output node\'s rule', () => {
    const loaded = loadPipeline('P', modelWithAChoice())!;

    expect(loaded.nodes.find((n) => n.id === 'N1')!.choices).toEqual([{ id: 'C1', name: 'Large', when: 'outputs.out > 10' }]);
    expect(loaded.nodes.find((n) => n.id === 'END')!.rule).toBe('$count(failures) = 0');
    expect(loaded.nodes.find((n) => n.id === 'N2')!.choices).toEqual([]);
  });

  it('writes each choice as a Thing the node holds, of the archetype bound to the choice role, with its rule', async () => {
    const model = modelWithAChoice();
    const nodes = [{ ...node('N1', 'A'), choices: [{ id: 'C1', name: 'Large', when: 'outputs.out > 10' }, { id: 'C2', name: 'Small', when: 'outputs.out <= 10' }] }, node('N2', 'B')];

    await savePipeline('MyPipeline', nodes, [], model, 'P');

    const written = fragment();
    expect(written.Things.find((t) => t.Id === 'C2')).toMatchObject({ Name: 'Small', Properties: { when: { value: 'outputs.out <= 10' } } });
    expect(written.Relationships).toContainEqual(expect.objectContaining({ Subject: 'C2', Predicate: 'is', Target: 'arch-choice' }));
    expect(written.Relationships).toContainEqual(expect.objectContaining({ Subject: 'N1', Predicate: 'has', Target: 'C2' }));
    expect(thingRemove).not.toHaveBeenCalledWith('C1');
  });

  it('retracts a choice the node no longer holds', async () => {
    await savePipeline('MyPipeline', [{ ...node('N1', 'A'), choices: [] }, node('N2', 'B')], [], modelWithAChoice(), 'P');

    expect(thingRemove).toHaveBeenCalledWith('C1');
  });

  it('writes the Output node\'s rule, and an empty one when the rule is taken away', async () => {
    const model = modelWithAChoice();
    const end = (rule?: string): EditorNode => ({ ...node('END', 'Finish', ''), kind: 'output', rule });

    await savePipeline('MyPipeline', [node('N1', 'A'), end('$count(failures) < 2')], [], model, 'P');
    await savePipeline('MyPipeline', [node('N1', 'A'), end(undefined)], [], model, 'P');

    const [withRule, without] = applyFragment.mock.calls.map((call) => JSON.parse(call[0]).Things.find((t: { Id: string }) => t.Id === 'END'));
    expect(withRule.Properties.when.value).toBe('$count(failures) < 2');
    expect(without.Properties.when.value).toBe('');
  });

  it('names what it did not find when a node holds a choice and the model binds nothing to the choice role', async () => {
    const { things, relationships } = graphWithVocabulary();
    things.splice(things.findIndex((t) => t.Id === 'arch-choice'), 1);

    await expect(savePipeline('First', [{ ...node('N1', 'A'), choices: [{ id: 'C9', name: 'Any', when: 'true' }] }], [],
      new PipelineModel(...withRoles(things, relationships)))).rejects.toThrow(/choice role/);
    expect(applyFragment).not.toHaveBeenCalled();
  });
});
