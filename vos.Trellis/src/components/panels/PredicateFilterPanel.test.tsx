import { describe, it, expect, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import { PredicateFilterPanel } from './PredicateFilterPanel';
import { useUiStore } from '../../stores/uiStore';

/**
 * Bug #5388 — the filter cluster on GraphPage put two panels with
 * `max-h-[40vh]` lists in a single bottom-anchored container, which pushed
 * the predicate panel header off-screen on tall lists and trapped scroll
 * inside the inner ul. The fix replaces the fixed 40vh cap with flex-1
 * min-h-0 so the panel uses its share of the parent flex container.
 *
 * These tests guard against regressing back to the absolute height cap.
 */
describe('PredicateFilterPanel layout (Bug #5388)', () => {
  beforeEach(() => {
    useUiStore.setState({
      predicateStats: [
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
});
