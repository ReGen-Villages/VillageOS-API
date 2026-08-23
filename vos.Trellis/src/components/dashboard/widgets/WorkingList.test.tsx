import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { WorkingList } from './WorkingList';
import type { WorkingWidget } from '../../../types/dashboard';
import type { ResolveContext } from '../../../api/dashboardApi';

vi.mock('react-i18next', () => ({
  useTranslation: () => ({ t: (key: string) => key }),
}));

const resolved = new Map<string, unknown>();
const stillResolving = { value: false };
vi.mock('../../../hooks/useDashboard', () => ({
  useBindings: (bindings: { kind: string }[]) =>
    bindings.map((b) => ({
      value: stillResolving.value ? null : resolved.get(b.kind) ?? null,
      loading: stillResolving.value,
    })),
}));

function widgetShowing(label: string): WorkingWidget {
  return {
    type: 'working',
    rows: [{
      label,
      value: { kind: 'property', thing: '$scope', property: 'pctOfConsumption' },
      working: { kind: 'working', property: 'pctOfConsumption' },
      format: 'pct100',
    }],
  };
}

const anyContext = {} as ResolveContext;

describe('WorkingList', () => {
  it('shows the formula the model holds and every input it reads', () => {
    resolved.set('property', 110.5);
    resolved.set('working', [
      { formula: 'generated / consumed * 100', term: 'generated', value: 4420 },
      { formula: 'generated / consumed * 100', term: 'consumed', value: 4000 },
    ]);

    render(<WorkingList widget={widgetShowing('Energy')} ctx={anyContext} />);

    expect(screen.getByText('generated / consumed * 100')).toBeTruthy();
    expect(screen.getByText('generated')).toBeTruthy();
    expect(screen.getByText('4,420')).toBeTruthy();
    expect(screen.getByText('consumed')).toBeTruthy();
    expect(screen.getByText('4,000')).toBeTruthy();
    expect(screen.getByText('111%')).toBeTruthy();
  });

  // A figure a service asserts has no working the model can account for. Anything shown here would be
  // Trellis describing arithmetic it does not run.
  it('says the model is given a figure it does not derive, and invents no formula', () => {
    resolved.set('property', 42);
    resolved.set('working', []);

    render(<WorkingList widget={widgetShowing('Food')} ctx={anyContext} />);

    expect(screen.getByText('widgets.working.notDerived')).toBeTruthy();
    expect(screen.queryByText(/\//)).toBeNull();
  });

  // A reduction reports its inputs and no formula: its working is a function over a set, and putting
  // that into words here would be the client saying what a figure means.
  it('lists the inputs of a reduction without a formula line', () => {
    resolved.set('property', 13500);
    resolved.set('working', [{ formula: null, term: 'areaM2', memberArchetype: 'SolarArray' }]);

    render(<WorkingList widget={widgetShowing('Array area')} ctx={anyContext} />);

    expect(screen.getByText('areaM2')).toBeTruthy();
    expect(screen.queryByText('widgets.working.notDerived')).toBeNull();
  });

  // A reduction's input is held by each member and not by the Thing computing the figure, so the row
  // names the archetype it is read off. An em dash there would read as the input being absent, which
  // is what a figure nobody computed shows.
  it('names the archetype a reduction reads its input off, in place of a value', () => {
    resolved.set('property', 13500);
    resolved.set('working', [{ formula: null, term: 'areaM2', memberArchetype: 'SolarArray' }]);

    render(<WorkingList widget={widgetShowing('Array area')} ctx={anyContext} />);

    expect(screen.getByText('SolarArray')).toBeTruthy();
    expect(screen.queryByText('—')).toBeNull();
  });

  // Nothing has resolved yet, so nothing is known about the figure. Saying the model is given it
  // states the one thing this widget exists to distinguish, before there is anything to say it about.
  it('says nothing about a figure while its bindings are still resolving', () => {
    stillResolving.value = true;
    try {
      render(<WorkingList widget={widgetShowing('Energy')} ctx={anyContext} />);
      expect(screen.queryByText('widgets.working.notDerived')).toBeNull();
    } finally {
      stillResolving.value = false;
    }
  });

  // A walk reaching several Things computing the figure returns each one's rows, and each carries its
  // own formula. Reading one off the first row would show the others' inputs under a formula that did
  // not produce them.
  it('keeps each formula with the inputs it produced', () => {
    resolved.set('property', 110.5);
    resolved.set('working', [
      { formula: 'generated / consumed * 100', term: 'generated', value: 4420 },
      { formula: 'produced / used * 100', term: 'produced', value: 900 },
    ]);

    render(<WorkingList widget={widgetShowing('Energy')} ctx={anyContext} />);

    expect(screen.getByText('generated / consumed * 100')).toBeTruthy();
    expect(screen.getByText('produced / used * 100')).toBeTruthy();
  });
});
