import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { DetailSpec } from '../../types/dashboard';
import { DetailEditor } from './DetailEditor';
import { catchmentContext } from './testOffers';

function commit(label: string, text: string) {
  const field = screen.getByLabelText(label);
  fireEvent.change(field, { target: { value: text } });
  fireEvent.blur(field);
}

describe('DetailEditor', () => {
  it('writes what titles the card, a group of properties, a relation followed from the kind, and the history switch', () => {
    let held: DetailSpec | undefined;
    const onChange = vi.fn((next: unknown) => { held = next as DetailSpec | undefined; });
    const draw = () => <DetailEditor label="Card" value={held} context={catchmentContext()} kind="Reservoir" onChange={onChange} />;
    const { rerender } = render(draw());

    commit('Titled by', 'capacity');
    expect(held).toEqual({ titleProperty: 'capacity' });
    rerender(draw());
    fireEvent.click(screen.getByRole('button', { name: 'Add a group' }));
    expect(held?.propertyGroups).toEqual([{ label: '', keys: [] }]);
    rerender(draw());
    commit('Properties', 'level');
    expect(held?.propertyGroups?.[0].keys).toEqual(['level']);
    rerender(draw());
    fireEvent.click(screen.getByRole('button', { name: 'Follow another link' }));
    rerender(draw());
    const offered = [...screen.getAllByLabelText('Link')[0].parentElement!.querySelectorAll('datalist option')].map((option) => option.getAttribute('value'));
    expect(offered).toEqual(['feeds', 'supplies']);
    commit('Link', 'supplies');
    expect(held?.relations?.[0]).toEqual({ predicate: 'supplies' });
    rerender(draw());
    fireEvent.click(screen.getByLabelText('Every property'));
    expect(held?.relations?.[0].properties).toBe('*');
    rerender(draw());
    fireEvent.click(screen.getByLabelText('Carries the handling history'));
    expect(held?.history).toEqual({ enabled: true });
    rerender(draw());
    fireEvent.click(screen.getByRole('button', { name: 'Remove supplies' }));
    expect(held?.relations).toBeUndefined();
  });

  it('takes the card away when its last key goes', () => {
    let held: DetailSpec | undefined = { titleProperty: 'capacity' };
    const onChange = vi.fn((next: unknown) => { held = next as DetailSpec | undefined; });
    render(<DetailEditor label="Card" value={held} context={catchmentContext()} kind="Reservoir" onChange={onChange} />);
    commit('Titled by', '');
    expect(held).toBeUndefined();
  });
});
