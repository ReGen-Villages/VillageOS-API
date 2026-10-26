import { describe, it, expect } from 'vitest';
import { validatePipeline, validateEnds, type ValidationNode, type ValidationEdge } from './validate';

const port = (portName: string, direction: 'in' | 'out', required = false, portId = portName): ValidationNode['ports'][number] => ({
  portId,
  portName,
  direction,
  type: '',
  required,
});

const node = (id: string, ports: ValidationNode['ports'], parameterBindings?: Record<string, string>): ValidationNode => ({
  id,
  label: id,
  ports,
  parameterBindings,
});

describe('validatePipeline', () => {
  it('reports a required input that is neither wired nor param-bound', () => {
    const nodes = [node('a', [port('in1', 'in', true)])];
    const issues = validatePipeline(nodes, []);
    expect(issues).toHaveLength(1);
    expect(issues[0]).toMatchObject({ kind: 'unbound-required-input', nodeId: 'a', port: 'in1' });
  });

  it('accepts a required input satisfied by a wire', () => {
    const nodes = [node('src', [port('out1', 'out')]), node('dst', [port('in1', 'in', true)])];
    const edges: ValidationEdge[] = [{ source: 'src', sourceHandle: 'out1', target: 'dst', targetHandle: 'in1' }];
    expect(validatePipeline(nodes, edges)).toEqual([]);
  });

  it('accepts a wire from an out port whose node also has an in port of that name, telling the two apart by identifier', () => {
    const nodes = [
      node('upper', [port('text', 'in', true, 'upper.in.text'), port('text', 'out', false, 'upper.out.text')]),
      node('rev', [port('text', 'in', true, 'rev.in.text')]),
    ];
    const edges: ValidationEdge[] = [{ source: 'upper', sourceHandle: 'upper.out.text', target: 'rev', targetHandle: 'rev.in.text' }];
    expect(validatePipeline(nodes, edges)).toEqual([
      expect.objectContaining({ kind: 'unbound-required-input', nodeId: 'upper', port: 'text' }),
    ]);
  });

  it('reports a wire whose port is gone by the node and the port name it still finds', () => {
    const nodes = [node('src', [port('out1', 'out', false, 'port-7')]), node('dst', [port('in1', 'in', false, 'port-8')])];
    const edges: ValidationEdge[] = [{ source: 'src', sourceHandle: 'port-7', target: 'dst', targetHandle: 'removed-port' }];
    expect(validatePipeline(nodes, edges)).toEqual([
      expect.objectContaining({ kind: 'dangling-wire', message: expect.stringContaining('src.out1') }),
    ]);
  });

  it('accepts a required input satisfied by a param binding', () => {
    const nodes = [node('a', [port('in1', 'in', true)], { in1: 'myParam' })];
    expect(validatePipeline(nodes, [])).toEqual([]);
  });

  it('does not flag optional inputs', () => {
    const nodes = [node('a', [port('in1', 'in', false)])];
    expect(validatePipeline(nodes, [])).toEqual([]);
  });

  it('reports a dangling wire whose target node does not exist', () => {
    const nodes = [node('src', [port('out1', 'out')])];
    const edges: ValidationEdge[] = [{ source: 'src', sourceHandle: 'out1', target: 'ghost', targetHandle: 'in1' }];
    const issues = validatePipeline(nodes, edges);
    expect(issues).toHaveLength(1);
    expect(issues[0].kind).toBe('dangling-wire');
  });

  it('reports a dangling wire whose port does not exist on an existing node', () => {
    const nodes = [node('src', [port('out1', 'out')]), node('dst', [port('in1', 'in')])];
    const edges: ValidationEdge[] = [{ source: 'src', sourceHandle: 'nope', target: 'dst', targetHandle: 'in1' }];
    expect(validatePipeline(nodes, edges).some((i) => i.kind === 'dangling-wire')).toBe(true);
  });

  it('returns no issues for a fully-wired valid pipeline', () => {
    const nodes = [node('src', [port('out1', 'out')]), node('dst', [port('in1', 'in', true)])];
    const edges: ValidationEdge[] = [{ source: 'src', sourceHandle: 'out1', target: 'dst', targetHandle: 'in1' }];
    expect(validatePipeline(nodes, edges)).toEqual([]);
  });
});

describe('validateEnds', () => {
  const node = (id: string, kind: 'input' | 'output', standsFor?: { name: string; mayStart: boolean; mayEnd: boolean }) =>
    ({ id, label: id, ports: [], kind, standsFor });

  it('refuses a start standing for something a run cannot come from', () => {
    const issues = validateEnds([node('start', 'input', { name: 'Reporting office', mayStart: false, mayEnd: true })], []);
    expect(issues).toEqual([{ kind: 'start-cannot-start', nodeId: 'start', named: 'Reporting office', message: expect.stringContaining('Reporting office') }]);
  });

  it('refuses an end standing for something a run cannot leave behind', () => {
    const issues = validateEnds([node('end', 'output', { name: 'below reorder', mayStart: true, mayEnd: false })], []);
    expect(issues).toEqual([{ kind: 'end-cannot-end', nodeId: 'end', named: 'below reorder', message: expect.stringContaining('below reorder') }]);
  });

  it('accepts an end standing for an external system, and a boundary node standing for nothing', () => {
    expect(validateEnds([
      node('end', 'output', { name: 'Reporting office', mayStart: false, mayEnd: true }),
      node('start', 'input'),
      { id: 'service', label: 'Echo', ports: [] },
    ], [])).toEqual([]);
  });
});
