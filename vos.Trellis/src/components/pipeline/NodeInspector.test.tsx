import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { NodeInspector } from './NodeInspector';
import type { PipelineNodeData } from './PipelineNodeView';

const port = (portId: string, portName: string, direction: 'in' | 'out') =>
  ({ portId, portName, direction, type: 'any', required: false });

function inspect(nodeId: string, data: PipelineNodeData, handlers: Partial<Parameters<typeof NodeInspector>[0]> = {}) {
  render(<NodeInspector nodeId={nodeId} data={data} wiredInputs={new Set()}
    onSetPorts={vi.fn()} onSetBinding={vi.fn()} onSetChoices={vi.fn()} onSetRule={vi.fn()} onClose={vi.fn()} {...handlers} />);
}

describe('NodeInspector', () => {
  it('shows an input a wire fills as wired, found by the port identifier the wire reaches', () => {
    const data = { label: 'Merge', ports: [port('merge.in.text', 'text', 'in'), port('merge.in.count', 'count', 'in')] };

    inspect('merge', data, { wiredInputs: new Set(['merge.in.text']) });

    expect(screen.getAllByText('wired')).toHaveLength(1);
    expect(screen.getByText('text').parentElement).toHaveTextContent('wired');
    expect(screen.getByText('count').parentElement).not.toHaveTextContent('wired');
  });

  it('adds a boundary port under an identifier of its own', () => {
    const onSetPorts = vi.fn();
    const data = { label: 'Start', kind: 'input' as const, ports: [port('start.out.seed', 'seed', 'out')] };

    inspect('start', data, { onSetPorts });
    fireEvent.click(screen.getByText('+ add port'));

    const [, ports] = onSetPorts.mock.calls[0];
    expect(ports).toHaveLength(2);
    expect(ports[1].portId).toEqual(expect.any(String));
    expect(ports[1].portId).not.toBe('start.out.seed');
  });

  it('starts a new Output rule as no fail reaching the node, for the person to edit', () => {
    const onSetRule = vi.fn();

    inspect('end', { label: 'Finish', kind: 'output', ports: [] }, { onSetRule });
    expect(screen.getByText(/the run fails if a fail wire reaches this node/)).toBeInTheDocument();
    fireEvent.click(screen.getByText('+ add rule'));

    expect(onSetRule).toHaveBeenCalledWith('end', '$count(failures) = 0');
  });

  it('shows the Output rule, edits it and takes it away', () => {
    const onSetRule = vi.fn();

    inspect('end', { label: 'Finish', kind: 'output', ports: [], rule: '$count(failures) < 2' }, { onSetRule });
    const rule = screen.getByLabelText('Output rule');
    expect(rule).toHaveValue('$count(failures) < 2');
    fireEvent.change(rule, { target: { value: '$count(failures) < 3' } });
    fireEvent.click(screen.getByText('remove rule'));

    expect(onSetRule).toHaveBeenNthCalledWith(1, 'end', '$count(failures) < 3');
    expect(onSetRule).toHaveBeenNthCalledWith(2, 'end', undefined);
  });

  it('offers no rule on the start node', () => {
    inspect('start', { label: 'Start', kind: 'input', ports: [] });

    expect(screen.queryByText('+ add rule')).not.toBeInTheDocument();
  });

  it('adds a choice to a service node under an identifier of its own', () => {
    const onSetChoices = vi.fn();

    inspect('sort', { label: 'Sort', ports: [port('sort.in', 'in', 'in')] }, { onSetChoices });
    fireEvent.click(screen.getByText('+ add choice'));

    const [nodeId, choices] = onSetChoices.mock.calls[0];
    expect(nodeId).toBe('sort');
    expect(choices).toEqual([{ id: expect.any(String), name: 'choice1', when: '' }]);
  });

  it('renames a choice, edits its rule and removes it', () => {
    const onSetChoices = vi.fn();
    const large = { id: 'c1', name: 'Large', when: 'outputs.size > 10' };

    inspect('sort', { label: 'Sort', ports: [], choices: [large] }, { onSetChoices });
    fireEvent.change(screen.getByLabelText('Name of choice 1'), { target: { value: 'Big' } });
    fireEvent.change(screen.getByLabelText('Rule of choice 1'), { target: { value: 'outputs.size > 20' } });
    fireEvent.click(screen.getByLabelText('Remove choice Large'));

    expect(onSetChoices.mock.calls.map(([, choices]) => choices)).toEqual([
      [{ ...large, name: 'Big' }],
      [{ ...large, when: 'outputs.size > 20' }],
      [],
    ]);
  });
});
