import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent } from '@testing-library/react';
import { DeleteThingButton } from './DeleteThingButton';

describe('DeleteThingButton (US #5828)', () => {
  it('calls onDelete when clicked (the caller then confirms + retracts)', () => {
    const onDelete = vi.fn();
    const { getByRole } = render(<DeleteThingButton onDelete={onDelete} />);
    fireEvent.click(getByRole('button'));
    expect(onDelete).toHaveBeenCalledOnce();
  });
});
