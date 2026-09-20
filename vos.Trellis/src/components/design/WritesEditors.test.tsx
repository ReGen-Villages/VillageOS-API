import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { ActionRecords, AskedValue, FormWidget } from '../../types/dashboard';
import { ActionWritesEditor, AskedEditor, FormWritesEditor, PreviewEditor } from './WritesEditors';
import { catchmentContext } from './testOffers';

function commit(label: string, text: string) {
  const field = screen.getByLabelText(label);
  fireEvent.change(field, { target: { value: text } });
  fireEvent.blur(field);
}

describe('AskedEditor', () => {
  it('asks for another value, types it, offers a roster for a chosen one, and takes the last away', () => {
    let held: AskedValue[] | undefined;
    const onChange = vi.fn((next: AskedValue[] | undefined) => { held = next; });
    const draw = () => <AskedEditor label="Asks for" value={held} context={catchmentContext()} onChange={onChange} />;
    const { rerender } = render(draw());
    fireEvent.click(screen.getByRole('button', { name: 'Ask for another value' }));
    expect(held).toEqual([{ key: '', label: '' }]);
    rerender(draw());
    commit('Key', 'to');
    rerender(draw());
    fireEvent.change(screen.getByLabelText('Kind of value'), { target: { value: 'choice' } });
    expect(held).toEqual([{ key: 'to', label: '', kind: 'choice' }]);
    rerender(draw());
    fireEvent.change(screen.getByLabelText('Chosen from'), { target: { value: 'thingList' } });
    expect(held?.[0].options).toEqual({ kind: 'thingList' });
    rerender(draw());
    fireEvent.click(screen.getByLabelText('Optional'));
    expect(held?.[0].optional).toBe(true);
    rerender(draw());
    fireEvent.click(screen.getByRole('button', { name: 'Remove to' }));
    expect(held).toBeUndefined();
  });
});

describe('ActionWritesEditor', () => {
  it('offers the registered endpoints for the route, the kinds for the archetype, and adds a choice', () => {
    let held: ActionRecords = { via: '', choices: [] };
    const onChange = vi.fn((next: ActionRecords) => { held = next; });
    const draw = () => <ActionWritesEditor label="Writes" value={held} context={catchmentContext()} onChange={onChange} />;
    const { rerender } = render(draw());
    const offered = [...screen.getByLabelText('Posts to').parentElement!.querySelectorAll('datalist option')].map((option) => option.getAttribute('value'));
    expect(offered).toEqual(['/api/endpoints/intake']);
    commit('Posts to', '/api/endpoints/intake');
    expect(held.via).toBe('/api/endpoints/intake');
    rerender(draw());
    commit('Mints a Thing of kind', 'Reservoir');
    expect(held.archetype).toBe('Reservoir');
    rerender(draw());
    fireEvent.click(screen.getByLabelText('A row may be pressed again'));
    expect(held.repeatable).toBe(true);
    rerender(draw());
    fireEvent.click(screen.getByRole('button', { name: 'Add a choice' }));
    expect(held.choices).toEqual([{ label: '' }]);
    rerender(draw());
    commit('Act', 'fill');
    expect(held.choices[0]).toEqual({ label: '', act: 'fill' });
    rerender(draw());
    fireEvent.click(screen.getByRole('button', { name: 'Remove item 1' }));
    expect(held.choices).toEqual([]);
  });
});

describe('FormWritesEditor and PreviewEditor', () => {
  it('writes the route, the act and the kind minted, and a preview only once it has an act or a label', () => {
    let writes: FormWidget['writes'] = { via: '', act: '' };
    const onWrites = vi.fn((next: FormWidget['writes']) => { writes = next; });
    const { rerender } = render(<FormWritesEditor label="Writes" value={writes} context={catchmentContext()} onChange={onWrites} />);
    commit('Act', 'submit');
    expect(writes).toEqual({ via: '', act: 'submit' });
    rerender(<FormWritesEditor label="Writes" value={writes} context={catchmentContext()} onChange={onWrites} />);
    commit('Mints a Thing of kind', 'Village');
    expect(writes).toEqual({ via: '', act: 'submit', archetype: 'Village' });

    let preview: FormWidget['preview'];
    const onPreview = vi.fn((next: FormWidget['preview']) => { preview = next; });
    render(<PreviewEditor label="Preview" value={preview} onChange={onPreview} />);
    commit('Preview act', 'estimate');
    expect(preview).toEqual({ act: 'estimate', label: '' });
  });
});
