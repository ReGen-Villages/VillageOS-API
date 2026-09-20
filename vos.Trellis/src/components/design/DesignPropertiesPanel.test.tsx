import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import type { DashboardSpec, Widget } from '../../types/dashboard';
import type { DesignSelection } from '../../utils/designEdits';
import { DesignPropertiesPanel } from './DesignPropertiesPanel';

const figure = (title: string): Widget => ({ type: 'kpi', title, value: { kind: 'const', value: 1 }, placement: { column: 0, row: 0, width: 3, height: 3 } });

const page = (): DashboardSpec => ({
  title: 'Springs',
  designed: true,
  sections: [
    { title: 'Flow', layout: 'grid', widgets: [figure('Flowing')] },
    { title: 'Quality', layout: 'grid', widgets: [] },
  ],
});

/** Renders the panel over a page, applying each edit to the page the way the workbench does. */
function open(selection: DesignSelection) {
  let held = page();
  const onEdit = vi.fn((change: (spec: DashboardSpec) => DashboardSpec) => { held = change(held); });
  const onSelect = vi.fn();
  render(<DesignPropertiesPanel spec={held} selection={selection} onEdit={onEdit} onSelect={onSelect} />);
  return { edited: () => held, onSelect };
}

describe('the page', () => {
  it('writes its title, subtitle and cadence', () => {
    const { edited } = open({ on: 'page' });
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Springs by flow' } });
    fireEvent.change(screen.getByLabelText('Subtitle'), { target: { value: 'sampled hourly' } });
    fireEvent.change(screen.getByLabelText('Refresh every (seconds)'), { target: { value: '30' } });
    expect(edited()).toMatchObject({ title: 'Springs by flow', subtitle: 'sampled hourly', refreshSeconds: 30 });
  });
});

describe('a section', () => {
  it('writes its hint and cannot move earlier from the top', () => {
    const { edited } = open({ on: 'section', section: 0 });
    fireEvent.change(screen.getByLabelText('Hint'), { target: { value: 'litres a second' } });
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
