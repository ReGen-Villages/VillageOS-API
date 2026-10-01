import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent, screen } from '@testing-library/react';
import { RetypeRow } from './RetypeRow';
import type { VosThing } from '../../types/vos';

vi.mock('../common/toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

const thing = (Id: string, Name: string, IsArchetype?: boolean): VosThing =>
  ({ Id, Name, IsArchetype, Properties: {} } as unknown as VosThing);

describe('RetypeRow', () => {
  it('offers the declared types as the new type, and no member', () => {
    const things = [
      thing('is-predicate', 'is'),
      thing('reservoir-type', 'Reservoir', true),
      thing('upper-reservoir', 'Upper Reservoir', false),
    ];
    render(<RetypeRow thingId="upper-reservoir" things={things} relationships={[]} />);

    fireEvent.focus(screen.getByPlaceholderText('New archetype…'));

    expect(screen.getByRole('button', { name: /^Reservoir/ })).toBeTruthy();
    expect(screen.queryByRole('button', { name: /^Upper Reservoir/ })).toBeNull();
    expect(screen.queryByRole('button', { name: /^is/ })).toBeNull();
  });
});
