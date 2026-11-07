import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { ReactFlowProvider, type NodeProps } from '@xyflow/react';
import { PipelineNodeView } from './PipelineNodeView';

describe('PipelineNodeView', () => {
  it('gives each port a handle keyed by its identifier, so an in port and an out port of one name are two handles', () => {
    const ports = [
      { portId: 'upper.in.text', portName: 'text', direction: 'in' as const, type: 'string', required: true },
      { portId: 'upper.out.text', portName: 'text', direction: 'out' as const, type: 'string', required: false },
    ];
    const props = { id: 'upper', data: { label: 'Uppercase', ports } } as unknown as NodeProps;

    const { container } = render(<ReactFlowProvider><PipelineNodeView {...props} /></ReactFlowProvider>);

    const handles = [...container.querySelectorAll('[data-handleid]')].map((handle) =>
      [handle.getAttribute('data-handleid'), handle.classList.contains('source') ? 'out' : 'in']);
    expect(handles).toEqual([['upper.in.text', 'in'], ['upper.out.text', 'out']]);
  });

  it.each([
    ['input', 'input'],
    ['output', 'output'],
    ['tell', 'tell'],
    ['followOn', 'follow-on'],
    ['stop', 'stop'],
  ])('names a %s node’s kind in the page’s language where a service node names what it calls', (kind, named) => {
    const props = { id: 'node', data: { label: 'Node', kind, ports: [] } } as unknown as NodeProps;

    const { container } = render(<ReactFlowProvider><PipelineNodeView {...props} /></ReactFlowProvider>);

    expect(container.querySelector('span.font-mono.text-zinc-400')).toHaveTextContent(new RegExp(`^${named}$`));
  });

  it.each([
    ['one of three readings reached the node', { done: 1, reached: 1, spreadOver: 3 }, '1/1'],
    ['two of three items have finished', { done: 2, reached: 3, spreadOver: 3 }, '2/3'],
    ['the node ran over a single item', { done: 1, reached: 1, spreadOver: 1 }, null],
  ])('shows finished items out of those that reached it when %s', (_, progress, badge) => {
    const props = { id: 'tell', data: { label: 'Tell operator', subdomain: 'tell', ports: [], progress } } as unknown as NodeProps;

    const { container } = render(<ReactFlowProvider><PipelineNodeView {...props} /></ReactFlowProvider>);

    const shown = [...container.querySelectorAll('span.font-mono')].map((span) => span.textContent).filter((text) => text?.includes('/'));
    expect(shown).toEqual(badge === null ? [] : [badge]);
  });
});
