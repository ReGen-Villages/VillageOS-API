import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { TypeFilterPanel } from './TypeFilterPanel';
import { useModelStore } from '../../stores/modelStore';
import { useUiStore } from '../../stores/uiStore';
import type { VosThing, VosRelationship } from '@tendril-ui/tendril';
import { stubScreenWidth } from '../../testScreenWidth';
import { numberIn } from '../../i18n/numbers';

/** The same layout guard as PredicateFilterPanel.test.tsx: both panels share the filter-cluster layout. */
describe('TypeFilterPanel layout', () => {
  beforeEach(() => {
    const isPredicate: VosThing = { Id: 'is', Name: 'is', Properties: {} };
    const typeA: VosThing = { Id: 'tA', Name: 'TypeA', Properties: {} };
    const typeB: VosThing = { Id: 'tB', Name: 'TypeB', Properties: {} };
    const inst1: VosThing = { Id: 'i1', Name: 'Inst1', Properties: {} };
    const inst2: VosThing = { Id: 'i2', Name: 'Inst2', Properties: {} };

    const things: VosThing[] = [isPredicate, typeA, typeB, inst1, inst2];
    const relationships: VosRelationship[] = [
      { Id: 'r1', Name: 'rel-A', SubjectId: 'i1', PredicateId: 'is', TargetId: 'tA', Properties: {} },
      { Id: 'r2', Name: 'rel-B', SubjectId: 'i2', PredicateId: 'is', TargetId: 'tB', Properties: {} },
    ];

    useModelStore.setState({ things, relationships });
    useUiStore.setState({ hiddenTypeIds: new Set<string>() });
  });

  it('inner list uses flex-based sizing, not a fixed max-h cap', () => {
    const { container } = render(<TypeFilterPanel />);
    const list = container.querySelector('ul');
    expect(list).not.toBeNull();
    expect(list!.className).toContain('flex-1');
    expect(list!.className).toContain('min-h-0');
    expect(list!.className).toContain('overflow-y-auto');
    expect(list!.className).not.toContain('max-h-[40vh]');
  });

  it('expanded panel is a flex column that takes its share of the parent', () => {
    const { container } = render(<TypeFilterPanel />);
    const root = container.firstElementChild as HTMLElement;
    expect(root).not.toBeNull();
    expect(root.className).toContain('flex');
    expect(root.className).toContain('flex-col');
    expect(root.className).toContain('flex-1');
    expect(root.className).toContain('min-h-0');
  });

  it('narrows the list to the types the search names, and shows every one again once it is cleared', () => {
    render(<TypeFilterPanel />);

    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'TypeB' } });
    expect(screen.queryByText('TypeA')).toBeNull();
    expect(screen.getByText('TypeB')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Clear search' }));
    expect(screen.getByText('TypeA')).toBeInTheDocument();
  });

  it('hides every type and the Things with none with None, says how many that hides, and shows them all again with All', () => {
    render(<TypeFilterPanel />);

    fireEvent.click(screen.getByRole('button', { name: 'None' }));
    expect(useUiStore.getState().hiddenTypeIds).toEqual(new Set(['tA', 'tB', '__noType__']));
    const total = screen.getByText(/^0\/\d+$/).textContent!.split('/')[1];
    expect(screen.getByText(`${total} hidden`)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'All' }));
    expect(useUiStore.getState().hiddenTypeIds.size).toBe(0);
    expect(screen.queryByText(/ hidden$/)).toBeNull();
  });

  describe('on a screen narrower than a tablet', () => {
    beforeEach(() => {
      stubScreenWidth(false);
    });

    afterEach(() => {
      vi.unstubAllGlobals();
    });

    it('starts closed, so it does not cover the graph', () => {
      render(<TypeFilterPanel />);

      expect(screen.getByRole('button', { expanded: false })).toBeInTheDocument();
    });
  });
});

describe('TypeFilterPanel header count', () => {
  it('writes how many Things it shows, and of how many, in the page language', () => {
    const instances: VosThing[] = Array.from({ length: 1500 }, (_, at) => ({ Id: `i${at}`, Name: `Inst${at}`, Properties: {} }));
    useModelStore.setState({
      things: [{ Id: 'is', Name: 'is', Properties: {} }, { Id: 'tA', Name: 'TypeA', Properties: {} }, ...instances],
      relationships: instances.map((instance, at) => (
        { Id: `r${at}`, Name: `rel-${at}`, SubjectId: instance.Id, PredicateId: 'is', TargetId: 'tA', Properties: {} })),
    });
    useUiStore.setState({ hiddenTypeIds: new Set<string>() });

    render(<TypeFilterPanel />);

    const everyThing = instances.length + 2;
    expect(screen.getByText(`${numberIn(everyThing)}/${numberIn(everyThing)}`)).toBeInTheDocument();
  });
});
