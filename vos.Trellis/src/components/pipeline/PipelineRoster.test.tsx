import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { PipelineRoster } from './PipelineRoster';

const pipelines = [
  { id: 'a', name: 'Readings arrive', nodeCount: 2 },
  { id: 'b', name: 'Refill', nodeCount: 1 },
];

describe('PipelineRoster', () => {
  it('lists every pipeline with its node count and marks the open one', () => {
    render(<PipelineRoster pipelines={pipelines} openedPipelineId="b" onOpen={vi.fn()} onCreate={vi.fn()} />);
    expect(screen.getByText('2 nodes')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Refill/ })).toHaveAttribute('aria-current', 'true');
    expect(screen.getByRole('button', { name: /Readings arrive/ })).not.toHaveAttribute('aria-current');
  });

  it('opens a pipeline when pressed', () => {
    const onOpen = vi.fn();
    render(<PipelineRoster pipelines={pipelines} openedPipelineId={null} onOpen={onOpen} onCreate={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: /Readings arrive/ }));
    expect(onOpen).toHaveBeenCalledWith('a');
  });

  it('names a new pipeline on Enter and clears the field, and ignores a blank name', () => {
    const onCreate = vi.fn();
    render(<PipelineRoster pipelines={[]} openedPipelineId={null} onOpen={vi.fn()} onCreate={onCreate} />);
    expect(screen.getByText('No pipeline yet')).toBeInTheDocument();
    const field = screen.getByRole('textbox', { name: 'Name a new pipeline' });
    fireEvent.change(field, { target: { value: '   ' } });
    fireEvent.keyDown(field, { key: 'Enter' });
    expect(onCreate).not.toHaveBeenCalled();
    fireEvent.change(field, { target: { value: 'Digest' } });
    fireEvent.keyDown(field, { key: 'Enter' });
    expect(onCreate).toHaveBeenCalledWith('Digest');
    expect(field).toHaveValue('');
  });
});
