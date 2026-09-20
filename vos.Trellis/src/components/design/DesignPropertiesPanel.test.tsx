import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { DashboardSpecification, Widget } from '../../types/dashboard';
import type { DesignSelection } from '../../utils/designEdits';
import { DesignPropertiesPanel } from './DesignPropertiesPanel';
import { catchmentContext } from './testOffers';

const figure = (title: string): Widget => ({ type: 'kpi', title, value: { kind: 'const', value: 1 }, placement: { column: 0, row: 0, width: 3, height: 3 } });

const page = (): DashboardSpecification => ({
  title: 'Springs',
  designed: true,
  sections: [
    { title: 'Flow', layout: 'grid', widgets: [figure('Flowing')] },
    { title: 'Quality', layout: 'grid', widgets: [] },
  ],
});

/** Renders the panel over a page, applying each edit to the page the way the workbench does. */
function open(selection: DesignSelection, specification: DashboardSpecification = page()) {
  let held = specification;
  const onEdit = vi.fn((change: (specification: DashboardSpecification) => DashboardSpecification) => { held = change(held); });
  const onSelect = vi.fn();
  const draw = () => <DesignPropertiesPanel specification={held} selection={selection} context={catchmentContext()} onEdit={onEdit} onSelect={onSelect} />;
  const { rerender } = render(draw());
  return { edited: () => held, onSelect, redraw: () => rerender(draw()) };
}

describe('the page', () => {
  it('writes its title, subtitle and cadence', () => {
    const { edited } = open({ on: 'page' });
    for (const [label, value] of [['Title', 'Springs by flow'], ['Subtitle', 'sampled hourly'], ['Refresh every (seconds)', '30']]) {
      fireEvent.change(screen.getByLabelText(label), { target: { value } });
      fireEvent.blur(screen.getByLabelText(label));
    }
    expect(edited()).toMatchObject({ title: 'Springs by flow', subtitle: 'sampled hourly', refreshSeconds: 30 });
  });
});

describe('a section', () => {
  it('writes its hint and cannot move earlier from the top', () => {
    const { edited } = open({ on: 'section', section: 0 });
    fireEvent.change(screen.getByLabelText('Hint'), { target: { value: 'litres a second' } });
    fireEvent.blur(screen.getByLabelText('Hint'));
    expect(edited().sections[0].hint).toBe('litres a second');
    expect(screen.getByRole('button', { name: 'Earlier' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'Later' })).toBeEnabled();
  });

  it('moves later and follows the selection to where the section went', () => {
    const { edited, onSelect } = open({ on: 'section', section: 0 });
    fireEvent.click(screen.getByRole('button', { name: 'Later' }));
    expect(edited().sections.map((section) => section.title)).toEqual(['Quality', 'Flow']);
    expect(onSelect).toHaveBeenCalledWith({ on: 'section', section: 1 });
  });
});

describe('a widget', () => {
  it('draws its schema: a figure offers its value binding and its format', () => {
    open({ on: 'widget', section: 0, widget: 0 });
    expect(screen.getAllByLabelText('Value')[0]).toHaveValue('const');
    expect(screen.getByLabelText('Number format')).toBeInTheDocument();
    expect(screen.getByLabelText('Trace')).toHaveValue('');
  });

  it('writes a field of the schema and takes an emptied one away', () => {
    const { edited, redraw } = open({ on: 'widget', section: 0, widget: 0 });
    fireEvent.change(screen.getByLabelText('Unit'), { target: { value: 'litres' } });
    fireEvent.blur(screen.getByLabelText('Unit'));
    expect((edited().sections[0].widgets[0] as { unit?: string }).unit).toBe('litres');
    redraw();
    fireEvent.change(screen.getByLabelText('Unit'), { target: { value: '' } });
    fireEvent.blur(screen.getByLabelText('Unit'));
    expect('unit' in edited().sections[0].widgets[0]).toBe(false);
  });

  it('moves to another section, landing under what that section holds, and the selection follows', () => {
    const { edited, onSelect } = open({ on: 'widget', section: 0, widget: 0 });
    fireEvent.change(screen.getByLabelText('In section'), { target: { value: '1' } });
    const moved = edited();
    expect(moved.sections[0].widgets).toHaveLength(0);
    expect(moved.sections[1].widgets[0]).toMatchObject({ title: 'Flowing', placement: { column: 0, row: 0, width: 3, height: 3 } });
    expect(onSelect).toHaveBeenCalledWith({ on: 'widget', section: 1, widget: 0 });
  });

  it('is removed, and nothing stays selected', () => {
    const { edited, onSelect } = open({ on: 'widget', section: 0, widget: 0 });
    fireEvent.click(screen.getByRole('button', { name: 'Remove widget' }));
    expect(edited().sections[0].widgets).toHaveLength(0);
    expect(onSelect).toHaveBeenCalledWith(null);
  });
});
