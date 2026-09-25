import type { PortInformation } from './model';
import i18n from '../i18n';

// Pure pre-run validation for the pipeline editor: explains why a DAG will not run before the
// user hits Run, instead of a silent dispatch failure. Kept pure so it is trivially unit-tested and can
// drive both a canvas marker and the Run button's enabled state.

export interface ValidationNode {
  id: string;
  label: string;
  ports: PortInformation[];
  paramBindings?: Record<string, string>;
}

export interface ValidationEdge {
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

export function validatePipeline(nodes: ValidationNode[], edges: ValidationEdge[]): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  const byId = new Map(nodes.map((n) => [n.id, n]));
  const hasPort = (n: ValidationNode, name: string, direction: 'in' | 'out') =>
    n.ports.some((p) => p.portName === name && p.direction === direction);

  // A wire is dangling if either endpoint node, or the named port on it, no longer exists. Only
  // well-formed wires count toward satisfying a required input.
  const wiredInputs = new Set<string>();
  for (const e of edges) {
    const source = byId.get(e.source);
    const dst = byId.get(e.target);
    const wellFormed =
      source !== undefined &&
      dst !== undefined &&
      hasPort(source, e.sourceHandle, 'out') &&
      hasPort(dst, e.targetHandle, 'in');
    if (!wellFormed) {
      issues.push({
        kind: 'dangling-wire',
        source: e.source,
        sourceHandle: e.sourceHandle,
        target: e.target,
        targetHandle: e.targetHandle,
        message: i18n.t('pipeline.danglingWire', { from: `${e.source}.${e.sourceHandle}`, to: `${e.target}.${e.targetHandle}` }),
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
          message: i18n.t('pipeline.requiredInputUnbound', { port: p.portName, node: n.label }),
        });
      }
    }
  }

  return issues;
}
