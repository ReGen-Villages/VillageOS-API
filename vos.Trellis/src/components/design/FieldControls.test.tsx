import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { FieldSpecification } from '../../utils/widgetSchema';
import { FieldControl } from './FieldControls';
import { catchmentContext } from './testOffers';

function open(field: FieldSpecification, value: unknown, kind?: string, family: 'widgetField' | 'bindingField' = 'widgetField') {
  let held = value;
  const onChange = vi.fn((next: unknown) => { held = next; });
  const draw = () => <FieldControl field={field} value={held} family={family} context={catchmentContext()} kind={kind} onChange={onChange} />;
  const { rerender } = render(draw());
  return { written: () => held, redraw: () => rerender(draw()) };
}

describe('FieldControl', () => {
  it('writes a number, and takes it away when emptied', () => {
    const { written, redraw } = open({ key: 'target', kind: 'number' }, 3);
    fireEvent.change(screen.getByLabelText('Target'), { target: { value: '12' } });
    fireEvent.blur(screen.getByLabelText('Target'));
    expect(written()).toBe(12);
    redraw();
    fireEvent.change(screen.getByLabelText('Target'), { target: { value: '' } });
    fireEvent.blur(screen.getByLabelText('Target'));
    expect(written()).toBeUndefined();
  });

  it('collects words as chips, offering the row keys not yet chosen', () => {
    const { written, redraw } = open({ key: 'searchKeys', kind: 'strings', offers: 'property' }, ['flow'], 'Spring');
    expect(screen.getByText('flow')).toBeInTheDocument();
    const offered = [...document.querySelectorAll('datalist option')].map((option) => option.getAttribute('value'));
    expect(offered).toEqual(['name']);
    fireEvent.change(screen.getByLabelText('Searched on'), { target: { value: 'name' } });
    fireEvent.blur(screen.getByLabelText('Searched on'));
    expect(written()).toEqual(['flow', 'name']);
    redraw();
    fireEvent.click(screen.getByRole('button', { name: 'Remove flow' }));
    expect(written()).toEqual(['name']);
  });

  it('writes a pair only once both halves are there', () => {
    const { written } = open({ key: 'band', kind: 'numberPair' }, undefined);
    fireEvent.change(screen.getByLabelText('Low'), { target: { value: '2' } });
    fireEvent.blur(screen.getByLabelText('Low'));
    expect(written()).toBeUndefined();
    fireEvent.change(screen.getByLabelText('High'), { target: { value: '8' } });
    fireEvent.blur(screen.getByLabelText('High'));
    expect(written()).toEqual([2, 8]);
  });

  it('refuses text that is not JSON and writes nothing', () => {
    const { written } = open({ key: 'body', kind: 'json' }, { a: 1 }, undefined, 'bindingField');
    fireEvent.change(screen.getByLabelText('Body'), { target: { value: '{ not json' } });
    fireEvent.blur(screen.getByLabelText('Body'));
    expect(screen.getByText('Not valid JSON; nothing was written.')).toBeInTheDocument();
    expect(written()).toEqual({ a: 1 });
  });

  it('adds an item to a list, edits its fields, and takes the list away with its last item', () => {
    const { written, redraw } = open({ key: 'buckets', kind: 'list', of: [{ key: 'label', kind: 'text' }, { key: 'severity', kind: 'choice', options: ['good', 'warn'] }] }, undefined);
    fireEvent.click(screen.getByRole('button', { name: 'Add' }));
    expect(written()).toEqual([{}]);
    redraw();
    fireEvent.change(screen.getByLabelText('Severity'), { target: { value: 'warn' } });
    expect(written()).toEqual([{ severity: 'warn' }]);
    redraw();
    fireEvent.click(screen.getByRole('button', { name: 'Remove item 1' }));
    expect(written()).toBeUndefined();
  });

  it('writes a group key by key and takes the group away when its last key goes', () => {
    const { written, redraw } = open({ key: 'sun', kind: 'group', of: [{ key: 'latitude', kind: 'binding', shape: 'number' }] }, { latitude: { kind: 'const', value: 1 } });
    fireEvent.change(screen.getByLabelText('Latitude'), { target: { value: '' } });
    expect(written()).toBeUndefined();
    redraw();
    fireEvent.change(screen.getByLabelText('Latitude'), { target: { value: 'property' } });
    expect(written()).toEqual({ latitude: { kind: 'property' } });
  });

  it('edits a table column with the column fields', () => {
    const { written } = open({ key: 'columns', kind: 'columns' }, undefined, 'Spring');
    fireEvent.click(screen.getByRole('button', { name: 'Add a column' }));
    expect(written()).toEqual([{ key: '', label: '' }]);
  });

  it('switches a bound number between a typed number and a binding', () => {
    const { written, redraw } = open({ key: 'threshold', kind: 'bound' }, 4);
    fireEvent.click(screen.getByRole('button', { name: 'Read from the model instead' }));
    expect(written()).toBeUndefined();
    redraw();
    fireEvent.change(screen.getByLabelText('Threshold'), { target: { value: 'const' } });
    expect(written()).toEqual({ kind: 'const' });
  });
});
