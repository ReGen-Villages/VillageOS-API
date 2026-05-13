import { describe, it, expect, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import { TypeFilterPanel } from './TypeFilterPanel';
import { useModelStore } from '../../stores/modelStore';
import { useUiStore } from '../../stores/uiStore';
import type { VosThing, VosRelationship } from '../../types/vos';

/**
 * Bug #5388 — see PredicateFilterPanel.test.tsx for context. Mirror guard
 * for the Type panel since both share the filter-cluster layout in
 * GraphPage and were both regressed by the same fixed max-h-[40vh] cap.
 */
describe('TypeFilterPanel layout (Bug #5388)', () => {
  beforeEach(() => {
    const isPred: VosThing = { Id: 'is', Name: 'is', Properties: {} };
    const typeA: VosThing = { Id: 'tA', Name: 'TypeA', Properties: {} };
    const typeB: VosThing = { Id: 'tB', Name: 'TypeB', Properties: {} };
    const inst1: VosThing = { Id: 'i1', Name: 'Inst1', Properties: {} };
    const inst2: VosThing = { Id: 'i2', Name: 'Inst2', Properties: {} };

    const things: VosThing[] = [isPred, typeA, typeB, inst1, inst2];
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
});
