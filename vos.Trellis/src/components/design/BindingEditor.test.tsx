import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { Binding } from '../../types/dashboard';
import { BindingEditor } from './BindingEditor';
import { catchmentContext } from './testOffers';

function open(value: Binding | undefined, compareKind?: string) {
  let held = value;
  const onChange = vi.fn((next: Binding | undefined) => { held = next; });
  const draw = () => <BindingEditor label="Rows" value={held} shape="rows" context={catchmentContext(compareKind)} onChange={onChange} />;
  const { rerender } = render(draw());
  return { bound: () => held, redraw: () => rerender(draw()) };
}

function commit(label: string, text: string) {
  const field = screen.getByLabelText(label);
  fireEvent.change(field, { target: { value: text } });
  fireEvent.blur(field);
}

describe('BindingEditor', () => {
  it('offers the kinds that fit the slot first and starts a binding of the chosen kind', () => {
    const { bound } = open(undefined);
    const options = [...screen.getByLabelText('Rows').querySelectorAll('option')].map((option) => option.value);
    expect(options.slice(0, 4)).toEqual(['', 'stateList', 'thingList', 'compareEntities']);
    fireEvent.change(screen.getByLabelText('Rows'), { target: { value: 'stateList' } });
    expect(bound()).toEqual({ kind: 'stateList' });
  });

  it('says what the chosen kind costs and offers its fields from the model: kinds, then the states that kind derives', () => {
    const { bound, redraw } = open({ kind: 'stateList', state: '', archetype: '' } as Binding);
    expect(screen.getByText('One question to the platform; the rows arrive with the values the widget reads.')).toBeInTheDocument();
    commit('Kind', 'Spring');
    expect(bound()).toMatchObject({ kind: 'stateList', archetype: 'Spring' });
    redraw();
    const offered = [...screen.getByLabelText('State').parentElement!.querySelectorAll('datalist option')].map((option) => option.getAttribute('value'));
    expect(offered).toEqual(['dry', 'flowing']);
    commit('State', 'flowing');
    expect(bound()).toMatchObject({ archetype: 'Spring', state: 'flowing' });
  });

  it('shows a derived field as words rather than a control', () => {
    open({ kind: 'stateList', state: 'flowing', archetype: 'Spring' });
    expect(screen.getByText(/Values sent with each row/)).toBeInTheDocument();
    expect(screen.queryByLabelText('Values sent with each row')).toBeNull();
  });

  it('builds a path a hop at a time from the links the reached kind carries, and says what it reaches', () => {
    const { bound, redraw } = open({ kind: 'related', via: [], thing: 'SPRING-1' });
    fireEvent.click(screen.getByRole('button', { name: 'Add a hop' }));
    expect(bound()).toMatchObject({ via: [{ predicate: '' }] });
    redraw();
    commit('Link', 'feeds');
    expect(bound()).toMatchObject({ via: [{ predicate: 'feeds', archetype: 'Reservoir' }] });
    redraw();
    expect(screen.getByText('reaches Reservoir')).toBeInTheDocument();
    expect(screen.getByText('1 hop per row')).toBeInTheDocument();
    const properties = [...screen.getByLabelText('Property').parentElement!.querySelectorAll('datalist option')].map((option) => option.getAttribute('value'));
    expect(properties).toEqual(['capacity', 'level']);
  });

  it('narrows to the compared Thing through a link the compared kind carries, settling the end where the model runs it one way', () => {
    const { bound, redraw } = open({ kind: 'stateCount', state: 'flowing', archetype: 'Spring' }, 'Reservoir');
    commit('Narrowed through', 'feeds');
    expect(bound()).toMatchObject({ scope: { viaPredicate: 'feeds', direction: 'in' } });
    redraw();
    expect(screen.getByText('Reservoir — feeds → Spring')).toBeInTheDocument();
    expect(screen.queryByLabelText('Which end the compared Thing stands on')).toBeNull();
  });

  it('adds a comparison typed as the property is', () => {
    const { bound, redraw } = open({ kind: 'thingList', archetype: 'Spring' });
    fireEvent.click(screen.getByRole('button', { name: 'Add a comparison' }));
    redraw();
    commit('Property', 'flow');
    redraw();
    commit('Value', '5');
    expect(bound()).toMatchObject({ where: [{ property: 'flow', op: '=', value: 5 }] });
  });

  it('adds a column worked out per row with the row as its scope', () => {
    const { bound, redraw } = open({ kind: 'thingList', archetype: 'Spring' });
    fireEvent.click(screen.getByRole('button', { name: 'Add a column worked out per row' }));
    expect(bound()).toMatchObject({ computed: [{ key: '', value: { kind: 'const', value: 0 } }] });
    redraw();
    commit('Key', 'share');
    expect(bound()).toMatchObject({ computed: [{ key: 'share' }] });
  });
});
