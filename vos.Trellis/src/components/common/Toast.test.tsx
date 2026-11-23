import { describe, it, expect, beforeEach } from 'vitest';
import { act, fireEvent, render, screen } from '@testing-library/react';
import { ToastContainer } from './Toast';
import { useToastStore } from './toastStore';

beforeEach(() => {
  useToastStore.setState({ toasts: [] });
});

describe('ToastContainer', () => {
  it('shows each toast in the store, and its close button removes it', () => {
    render(<ToastContainer />);
    act(() => useToastStore.getState().add('error', 'The file could not be read.'));

    expect(screen.getByText('The file could not be read.')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button'));

    expect(screen.queryByText('The file could not be read.')).not.toBeInTheDocument();
    expect(useToastStore.getState().toasts).toEqual([]);
  });
});
