import type { PortInfo } from './model';

// Pure pre-run validation for the pipeline editor: explains why a DAG will not run before the
// user hits Run, instead of a silent dispatch failure. Kept pure so it is trivially unit-tested and can
// drive both a canvas marker and the Run button's enabled state.

export interface ValNode {
  id: string;
  label: string;
  ports: PortInfo[];
  /** Input-port name -> run-param key. */
  paramBindings?: Record<string, string>;
}

export interface ValEdge {
  source: string;
  sourceHandle: string;
  target: string;
  targetHandle: string;
}

export type ValidationIssue =
  | { kind: 'unbound-required-input'; nodeId: string; port: string; message: string }
  | {
      kind: 'dangling-wire';
      source: string;
      sourceHandle: string;
      target: string;
      targetHandle: string;
      message: string;
    };

const key = (nodeId: string, port: string) => `${nodeId} ${port}`;

export function validatePipeline(nodes: ValNode[], edges: ValEdge[]): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const hasPort = (n: ValNode, name: string, dir: 'in' | 'out') =>
    n.ports.some((p) => p.portName === name && p.direction === dir);

  // A wire is dangling if either endpoint node, or the named port on it, no longer exists. Only
  // well-formed wires count toward satisfying a required input.
  const wiredInputs = new Set<string>();
  for (const e of edges) {
    const src = byId.get(e.source);
    const dst = byId.get(e.target);
    const wellFormed =
      src !== undefined &&
      dst !== undefined &&
      hasPort(src, e.sourceHandle, 'out') &&
      hasPort(dst, e.targetHandle, 'in');
    if (!wellFormed) {
      issues.push({
        kind: 'dangling-wire',
        source: e.source,
        sourceHandle: e.sourceHandle,
        target: e.target,
        targetHandle: e.targetHandle,
        message: `Dangling wire ${e.source}.${e.sourceHandle} → ${e.target}.${e.targetHandle}`,
      });
      continue;
    }
    wiredInputs.add(key(e.target, e.targetHandle));
  }

  // Every required input port must be satisfied by a wire or a run-param binding.
  for (const n of nodes) {
    for (const p of n.ports) {
      if (p.direction !== 'in' || !p.required) continue;
      const satisfied = wiredInputs.has(key(n.id, p.portName)) || Boolean(n.paramBindings?.[p.portName]);
      if (!satisfied) {
        issues.push({
          kind: 'unbound-required-input',
          nodeId: n.id,
          port: p.portName,
          message: `Required input '${p.portName}' on '${n.label}' is neither wired nor bound to a run param`,
        });
      }
    }
  }

  return issues;
}
