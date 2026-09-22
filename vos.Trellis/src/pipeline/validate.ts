import type { PortInformation } from './model';

// Pure pre-run validation for the pipeline editor: explains why a DAG will not run before the
// user hits Run, instead of a silent dispatch failure. Kept pure so it is trivially unit-tested and can
// drive both a canvas marker and the Run button's enabled state.

export interface ValidationNode {
  id: string;
  label: string;
  ports: PortInformation[];
  paramBindings?: Record<string, string>;
  /** A boundary node: where a run comes from, or what it leaves behind. */
  kind?: 'input' | 'output';
  /** What a boundary node stands for, and which end it may be. */
  standsFor?: { name: string; mayStart: boolean; mayEnd: boolean };
}

export interface ValidationEdge {
  source: string;
  sourceHandle: string;
  target: string;
  targetHandle: string;
}

export type ValidationIssue =
  | { kind: 'unbound-required-input'; nodeId: string; port: string; message: string }
  | EndIssue
  | {
      kind: 'dangling-wire';
      source: string;
      sourceHandle: string;
      target: string;
      targetHandle: string;
      message: string;
    };

export type EndIssue =
  | { kind: 'start-cannot-start'; nodeId: string; named: string; message: string }
  | { kind: 'end-cannot-end'; nodeId: string; named: string; message: string };

const key = (nodeId: string, port: string) => `${nodeId} ${port}`;

/** Whether each end of the drawing stands for something a run may come from or leave behind. A boundary
 *  node standing for nothing is fine: at the start it is a run started by hand, at the end the answer.
 *  Judged on the drawing, before anything runs, because the orchestrator reads none of this. */
export function validateEnds(nodes: ValidationNode[], _edges: ValidationEdge[]): EndIssue[] {
  const issues: EndIssue[] = [];
  for (const node of nodes) {
    if (!node.kind || !node.standsFor) continue;
    if (node.kind === 'input' && !node.standsFor.mayStart)
      issues.push({
        kind: 'start-cannot-start', nodeId: node.id, named: node.standsFor.name,
        message: `'${node.label}' is where this pipeline starts and stands for '${node.standsFor.name}', which starts nothing`,
      });
    if (node.kind === 'output' && !node.standsFor.mayEnd)
      issues.push({
        kind: 'end-cannot-end', nodeId: node.id, named: node.standsFor.name,
        message: `'${node.label}' is where this pipeline ends and stands for '${node.standsFor.name}', which a run cannot leave behind`,
      });
  }
  return issues;
}

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
