import { describe, it, expect } from 'vitest';
import { validatePipeline, type ValNode, type ValEdge } from './validate';

const port = (portName: string, direction: 'in' | 'out', required = false): ValNode['ports'][number] => ({
  portName,
  direction,
  type: '',
  required,
});

const node = (id: string, ports: ValNode['ports'], paramBindings?: Record<string, string>): ValNode => ({
  id,
  label: id,
  ports,
  paramBindings,
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
    const edges: ValEdge[] = [{ source: 'src', sourceHandle: 'out1', target: 'dst', targetHandle: 'in1' }];
    expect(validatePipeline(nodes, edges)).toEqual([]);
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
    const edges: ValEdge[] = [{ source: 'src', sourceHandle: 'out1', target: 'ghost', targetHandle: 'in1' }];
    const issues = validatePipeline(nodes, edges);
    expect(issues).toHaveLength(1);
    expect(issues[0].kind).toBe('dangling-wire');
  });

  it('reports a dangling wire whose port does not exist on an existing node', () => {
    const nodes = [node('src', [port('out1', 'out')]), node('dst', [port('in1', 'in')])];
    const edges: ValEdge[] = [{ source: 'src', sourceHandle: 'nope', target: 'dst', targetHandle: 'in1' }];
    expect(validatePipeline(nodes, edges).some((i) => i.kind === 'dangling-wire')).toBe(true);
  });

  it('returns no issues for a fully-wired valid pipeline', () => {
    const nodes = [node('src', [port('out1', 'out')]), node('dst', [port('in1', 'in', true)])];
    const edges: ValEdge[] = [{ source: 'src', sourceHandle: 'out1', target: 'dst', targetHandle: 'in1' }];
    expect(validatePipeline(nodes, edges)).toEqual([]);
  });
});
