import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';

vi.mock('../common/toastStore', () => ({ toast: { information: vi.fn() } }));

import { toast } from '../common/toastStore';
import { CopyIdButton, EditModeToggle } from './DetailPanelControls';

describe('CopyIdButton', () => {
  it('copies the whole identifier and says so', () => {
    const writeText = vi.fn(() => Promise.resolve());
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const id = '0f8fad5b-d9cb-469f-a165-70867728950e';
    render(<CopyIdButton id={id} />);

    fireEvent.click(screen.getByRole('button'));

    expect(writeText).toHaveBeenCalledWith(id);
    expect(toast.information).toHaveBeenCalled();
  });
});

describe('EditModeToggle', () => {
  it('toggles when pressed, and not while it is disabled', () => {
    const onToggle = vi.fn();
    const { rerender } = render(<EditModeToggle editing={false} onToggle={onToggle} title="Edit properties" />);

    fireEvent.click(screen.getByRole('button', { name: 'Edit properties' }));
    expect(onToggle).toHaveBeenCalledTimes(1);

    rerender(<EditModeToggle editing={false} onToggle={onToggle} title="Edit properties" disabled />);
    fireEvent.click(screen.getByRole('button', { name: 'Edit properties' }));
    expect(onToggle).toHaveBeenCalledTimes(1);
  });
});
