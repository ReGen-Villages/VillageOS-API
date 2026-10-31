import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { WireInspector } from './WireInspector';

const results = [
  { id: 'success', name: 'success' },
  { id: 'fail', name: 'fail' },
  { id: 'c1', name: 'Large' },
];

function inspect(takenOn: string[], onSetTakenOn = vi.fn()) {
  render(<WireInspector wireId="w1" fromPortName="out" toPortName="in" paths={{}} onSetPath={vi.fn()}
    results={results} takenOn={takenOn} onSetTakenOn={onSetTakenOn} onClose={vi.fn()} />);
  return onSetTakenOn;
}

describe('WireInspector', () => {
  it('offers success, fail and each choice of the node the wire leaves, with what it is taken on ticked', () => {
    inspect(['success']);

    expect(screen.getByLabelText('success')).toBeChecked();
    expect(screen.getByLabelText('fail')).not.toBeChecked();
    expect(screen.getByLabelText('Large')).not.toBeChecked();
  });

  it('adds a result when it is ticked and drops one when it is cleared', () => {
    const onSetTakenOn = inspect(['success', 'c1']);

    fireEvent.click(screen.getByLabelText('fail'));
    fireEvent.click(screen.getByLabelText('success'));

    expect(onSetTakenOn).toHaveBeenNthCalledWith(1, 'w1', ['success', 'c1', 'fail']);
    expect(onSetTakenOn).toHaveBeenNthCalledWith(2, 'w1', ['c1']);
  });

  it('keeps the last result a wire is taken on, since a wire taken on nothing never carries', () => {
    inspect(['fail']);

    expect(screen.getByLabelText('fail')).toBeDisabled();
    expect(screen.getByLabelText('success')).toBeEnabled();
  });
});
