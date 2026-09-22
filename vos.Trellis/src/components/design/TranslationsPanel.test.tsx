import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { DashboardSpecification } from '../../types/dashboard';
import { TranslationsPanel } from './TranslationsPanel';

const specification: DashboardSpecification = {
  title: 'Springs',
  sections: [{ layout: 'grid', widgets: [{ type: 'kpi', title: 'Flowing', value: { kind: 'const', value: 1 } }] }],
  translations: { nl: { Springs: 'Bronnen' } },
};

describe('TranslationsPanel', () => {
  it('lists one row per display string and one column per language the console speaks, once per language block', () => {
    render(<TranslationsPanel specification={specification} onEdit={vi.fn()} onClose={vi.fn()} />);
    expect(screen.getAllByRole('columnheader').map((cell) => cell.textContent)).toEqual(['Base text', 'Deutsch', 'Español', 'Français', 'Italiano', 'Nederlands', 'العربية']);
    expect(screen.getByLabelText('Springs in Nederlands')).toHaveValue('Bronnen');
    expect(screen.getByLabelText('Flowing in Nederlands')).toHaveValue('');
  });

  it('writes a translation into its language block, and takes the block away when its last word goes', () => {
    let held = specification;
    const onEdit = vi.fn((change: (specification: DashboardSpecification) => DashboardSpecification) => { held = change(held); });
    render(<TranslationsPanel specification={specification} onEdit={onEdit} onClose={vi.fn()} />);
    const cell = screen.getByLabelText('Flowing in Deutsch');
    fireEvent.change(cell, { target: { value: 'Fließend' } });
    fireEvent.blur(cell);
    expect(held.translations).toEqual({ nl: { Springs: 'Bronnen' }, de: { Flowing: 'Fließend' } });
    const dutch = screen.getByLabelText('Springs in Nederlands');
    fireEvent.change(dutch, { target: { value: '' } });
    fireEvent.blur(dutch);
    expect(held.translations).toEqual({ de: { Flowing: 'Fließend' } });
  });
});
