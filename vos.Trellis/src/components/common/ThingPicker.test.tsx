import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { ThingPicker } from './ThingPicker';
import type { VosThing } from '../../types/vos';

const things = (count: number, prefix = 'Thing'): VosThing[] =>
  Array.from({ length: count }, (_, at) => ({ Id: `${prefix}-${at}`, Name: `${prefix} ${at}`, Properties: {} }));

describe('ThingPicker', () => {
  it('says how many Things it left out when it holds more than it shows', () => {
    render(<ThingPicker things={things(60)} value="" onChange={vi.fn()} />);
    fireEvent.focus(screen.getByRole('textbox'));

    expect(screen.getByRole('status')).toHaveTextContent('Showing 50 of 60 — type to narrow');
  });

  it('counts only the Things that match what was typed', () => {
    render(<ThingPicker things={[...things(55, 'Spring'), ...things(30, 'Reservoir')]} value="" onChange={vi.fn()} />);
    fireEvent.change(screen.getByRole('textbox'), { target: { value: 'spring' } });

    expect(screen.getByRole('status')).toHaveTextContent('Showing 50 of 55 — type to narrow');
  });

  it('says nothing when it shows every Thing it holds', () => {
    render(<ThingPicker things={things(50)} value="" onChange={vi.fn()} />);
    fireEvent.focus(screen.getByRole('textbox'));

    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
