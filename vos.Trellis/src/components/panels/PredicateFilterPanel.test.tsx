import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { PredicateFilterPanel } from './PredicateFilterPanel';
import { useUiStore } from '../../stores/uiStore';
import { stubScreenWidth } from '../../testScreenWidth';

/**
 * A fixed height cap on the list pushed the panel header off-screen on tall lists and trapped
 * scrolling inside the inner list, so the panel takes its share of the parent flex container instead.
 */
describe('PredicateFilterPanel layout', () => {
  beforeEach(() => {
    useUiStore.setState({
      predicateStatistics: [
        { predicateId: 'p1', predicateName: 'consumes', edgeCount: 35, color: '#aaa' },
        { predicateId: 'p2', predicateName: 'produces', edgeCount: 28, color: '#bbb' },
      ],
      hiddenPredicateIds: new Set<string>(),
    });
  });

  it('inner list uses flex-based sizing, not a fixed max-h cap', () => {
    const { container } = render(<PredicateFilterPanel />);
    const list = container.querySelector('ul');
    expect(list).not.toBeNull();
    expect(list!.className).toContain('flex-1');
    expect(list!.className).toContain('min-h-0');
    expect(list!.className).toContain('overflow-y-auto');
    expect(list!.className).not.toContain('max-h-[40vh]');
  });

  it('expanded panel is a flex column that takes its share of the parent', () => {
    const { container } = render(<PredicateFilterPanel />);
    const root = container.firstElementChild as HTMLElement;
    expect(root).not.toBeNull();
    expect(root.className).toContain('flex');
    expect(root.className).toContain('flex-col');
    expect(root.className).toContain('flex-1');
    expect(root.className).toContain('min-h-0');
  });

  it('narrows the list to the predicates the search names, and shows every one again once it is cleared', () => {
    render(<PredicateFilterPanel />);

    fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'prod' } });
    expect(screen.queryByText('consumes')).toBeNull();
    expect(screen.getByText('produces')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Clear search' }));
    expect(screen.getByText('consumes')).toBeInTheDocument();
  });

  it('hides every predicate with None, says how many edges that hides, and shows them all again with All', () => {
    render(<PredicateFilterPanel />);

    fireEvent.click(screen.getByRole('button', { name: 'None' }));
    expect(useUiStore.getState().hiddenPredicateIds).toEqual(new Set(['p1', 'p2']));
    expect(screen.getByText('63 hidden')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'All' }));
    expect(useUiStore.getState().hiddenPredicateIds.size).toBe(0);
    expect(screen.queryByText('63 hidden')).toBeNull();
  });

  describe('on a screen narrower than a tablet', () => {
    beforeEach(() => {
      stubScreenWidth(false);
    });

    afterEach(() => {
      vi.unstubAllGlobals();
    });

    it('starts closed, so it does not cover the graph', () => {
      render(<PredicateFilterPanel />);

      expect(screen.getByRole('button', { expanded: false })).toBeInTheDocument();
    });
  });
});
