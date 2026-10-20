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
});
