import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import { NewNameField } from './NewNameField';

function field(onName = vi.fn()) {
  render(<NewNameField label="New page name" addLabel={(name) => `Start a page named ${name}`} onName={onName} />);
  return screen.getByRole('textbox', { name: 'New page name' });
}

describe('NewNameField', () => {
  it('names the item on Enter, trimmed, and clears the field', () => {
    const onName = vi.fn();
    const input = field(onName);

    fireEvent.change(input, { target: { value: '  Overview  ' } });
    fireEvent.keyDown(input, { key: 'Enter' });

    expect(onName).toHaveBeenCalledWith('Overview');
    expect(input).toHaveValue('');
  });

  it('names the item from its button, which says what it will be called', () => {
    const onName = vi.fn();
    fireEvent.change(field(onName), { target: { value: 'Overview' } });

    fireEvent.click(screen.getByRole('button', { name: 'Start a page named Overview' }));

    expect(onName).toHaveBeenCalledWith('Overview');
  });

  it('ignores a blank name', () => {
    const onName = vi.fn();
    const input = field(onName);

    fireEvent.change(input, { target: { value: '   ' } });
    fireEvent.keyDown(input, { key: 'Enter' });
    fireEvent.click(screen.getByRole('button'));

    expect(onName).not.toHaveBeenCalled();
  });
});
