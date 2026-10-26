import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { NodeInspector } from './NodeInspector';

const port = (portId: string, portName: string, direction: 'in' | 'out') =>
  ({ portId, portName, direction, type: 'any', required: false });

describe('NodeInspector', () => {
  it('shows an input a wire fills as wired, found by the port identifier the wire reaches', () => {
    const data = { label: 'Merge', ports: [port('merge.in.text', 'text', 'in'), port('merge.in.count', 'count', 'in')] };

    render(<NodeInspector nodeId="merge" data={data} wiredInputs={new Set(['merge.in.text'])}
      onSetPorts={vi.fn()} onSetBinding={vi.fn()} onClose={vi.fn()} />);

    expect(screen.getAllByText('wired')).toHaveLength(1);
    expect(screen.getByText('text').parentElement).toHaveTextContent('wired');
    expect(screen.getByText('count').parentElement).not.toHaveTextContent('wired');
  });

  it('adds a boundary port under an identifier of its own', () => {
    const onSetPorts = vi.fn();
    const data = { label: 'Start', kind: 'input' as const, ports: [port('start.out.seed', 'seed', 'out')] };

    render(<NodeInspector nodeId="start" data={data} wiredInputs={new Set()}
      onSetPorts={onSetPorts} onSetBinding={vi.fn()} onClose={vi.fn()} />);
    fireEvent.click(screen.getByText('+ add port'));

    const [, ports] = onSetPorts.mock.calls[0];
    expect(ports).toHaveLength(2);
    expect(ports[1].portId).toEqual(expect.any(String));
    expect(ports[1].portId).not.toBe('start.out.seed');
  });
});
