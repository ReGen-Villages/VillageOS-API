import { describe, it, expect } from 'vitest';
import {
  withAlpha,
  applySelectionHighlight,
  brightenEdge,
  computeClusterNodeStyle,
  applyNodeFlash,
  applyEdgeFlash,
  BRIGHT_EDGE_COLOR,
  BRIGHT_EDGE_SIZE,
} from './reducerHelpers';
import type { FlashSettings } from './guiSettings';
import type { ClusterMap } from './predicateCluster';

const DEFAULT_FLASH: FlashSettings = {
  flashEdgeSize: 1.5,
  flashNodeSizeFactor: 1.4,
  flashNodeBrighten: 0.5,
};

// ── withAlpha ────────────────────────────────────────────────────────────

describe('withAlpha', () => {
  it('appends alpha to a 7-char hex (#rrggbb)', () => {
    expect(withAlpha('#ff0000', 0.5)).toBe('#ff000080');
    expect(withAlpha('#ff0000', 1)).toBe('#ff0000ff');
    expect(withAlpha('#ff0000', 0)).toBe('#ff000000');
  });

  it('replaces existing alpha in 9-char hex (#rrggbbaa)', () => {
    expect(withAlpha('#ff0000ff', 0.5)).toBe('#ff000080');
    expect(withAlpha('#aabbcc00', 1)).toBe('#aabbccff');
  });

  it('returns input unchanged for non-standard lengths', () => {
    expect(withAlpha('#fff', 0.5)).toBe('#fff');
    expect(withAlpha('red', 0.5)).toBe('red');
  });
});

// ── applySelectionHighlight ──────────────────────────────────────────────

describe('applySelectionHighlight', () => {
  const data = { color: '#ff0000', size: 5, label: 'Test' };

  it('adds highlighted + zIndex when node matches selectedNodeId', () => {
    const result = applySelectionHighlight('n1', data, 'n1');
    expect(result).toEqual({ ...data, highlighted: true, zIndex: 10 });
  });

  it('returns data unchanged when node does not match', () => {
    const result = applySelectionHighlight('n1', data, 'n2');
    expect(result).toBe(data);
  });

  it('returns data unchanged when selId is null', () => {
    const result = applySelectionHighlight('n1', data, null);
    expect(result).toBe(data);
  });
});

// ── brightenEdge ─────────────────────────────────────────────────────────

describe('brightenEdge', () => {
  it('returns bright color, size, and forceLabel', () => {
    const data = { color: '#333', size: 1, label: 'has' };
    const result = brightenEdge(data);
    expect(result.color).toBe(BRIGHT_EDGE_COLOR);
    expect(result.size).toBe(BRIGHT_EDGE_SIZE);
    expect(result.forceLabel).toBe(true);
    expect(result.label).toBe('has');
  });
});

// ── applyNodeFlash ──────────────────────────────────────────────────────

describe('applyNodeFlash', () => {
  it('brightens colour and increases size', () => {
    const data = { color: '#f87171', size: 10, label: 'Home' };
    const result = applyNodeFlash(data, DEFAULT_FLASH);
    expect(result.size).toBe(10 * DEFAULT_FLASH.flashNodeSizeFactor);
    expect(result.zIndex).toBe(10);
    expect(result.color).not.toBe('#f87171');
    expect((result.color as string).startsWith('#')).toBe(true);
  });

  it('uses fallback color when none provided', () => {
    const result = applyNodeFlash({ size: 5 }, DEFAULT_FLASH);
    expect((result.color as string).startsWith('#')).toBe(true);
  });

  it('uses fallback size when none provided', () => {
    const result = applyNodeFlash({ color: '#ff0000' }, DEFAULT_FLASH);
    expect(result.size).toBe(5 * DEFAULT_FLASH.flashNodeSizeFactor);
  });

  it('strips alpha channel before brightening', () => {
    const result = applyNodeFlash({ color: '#ff000080', size: 5 }, DEFAULT_FLASH);
    expect((result.color as string).length).toBe(7); // no alpha
  });

  it('uses custom flash settings', () => {
    const custom: FlashSettings = {
      flashEdgeSize: 3,
      flashNodeSizeFactor: 2.0,
      flashNodeBrighten: 0.8,
    };
    const result = applyNodeFlash({ color: '#f87171', size: 10 }, custom);
    expect(result.size).toBe(20); // 10 * 2.0
  });
});

// ── applyEdgeFlash ──────────────────────────────────────────────────────

describe('applyEdgeFlash', () => {
  it('brightens current edge color and sets size + forceLabel', () => {
    const data = { color: '#333333', size: 1, label: 'consumes' };
    const result = applyEdgeFlash(data, DEFAULT_FLASH);
    expect(result.color).not.toBe('#333333');
    expect((result.color as string).startsWith('#')).toBe(true);
    expect(result.size).toBe(DEFAULT_FLASH.flashEdgeSize);
    expect(result.forceLabel).toBe(true);
    expect(result.label).toBe('consumes');
  });

  it('uses fallback color when none provided', () => {
    const result = applyEdgeFlash({ size: 1 }, DEFAULT_FLASH);
    expect((result.color as string).startsWith('#')).toBe(true);
  });

  it('strips alpha channel before brightening', () => {
    const result = applyEdgeFlash({ color: '#33333380', size: 1 }, DEFAULT_FLASH);
    expect((result.color as string).length).toBe(7);
  });
});

// ── computeClusterNodeStyle ──────────────────────────────────────────────

describe('computeClusterNodeStyle', () => {
  const makeClusterMap = (): ClusterMap => ({
    nodeCluster: new Map([
      ['n1', 0], ['n2', 0], ['n3', 1], ['orphan', -1],
    ]),
    clusters: [new Set(['n1', 'n2']), new Set(['n3'])],
    representatives: new Map([[0, 'n1'], [1, 'n3']]),
  });

  it('dims predicate-type nodes', () => {
    const cm = makeClusterMap();
    const result = computeClusterNodeStyle('n1', { thingType: 'predicate', color: '#fff', size: 5 }, cm, new Set(), new Set(), null, false);
    expect(result.color).toBe('#3f3f46');
    expect(result.size).toBe(2);
    expect(result.label).toBe('');
  });

  it('shows expanded nodes at full opacity', () => {
    const cm = makeClusterMap();
    const expanded = new Set(['n2']);
    const result = computeClusterNodeStyle('n2', { thingType: 'default', color: '#ff0000' }, cm, new Set(), expanded, null, false);
    expect(result.zIndex).toBe(2);
    expect(result.color).toBe('#ff0000');
  });

  it('dims unclustered nodes', () => {
    const cm = makeClusterMap();
    const result = computeClusterNodeStyle('orphan', { thingType: 'default', color: '#ff0000', label: 'Orphan' }, cm, new Set(), new Set(), null, false);
    expect(result.color).toBe('#27272a');
    expect(result.label).toBe('');
  });

  it('hides non-representative collapsed cluster members', () => {
    const cm = makeClusterMap();
    const collapsed = new Set([0]);
    const result = computeClusterNodeStyle('n2', { thingType: 'default', color: '#ff0000' }, cm, collapsed, new Set(), null, false);
    expect(result.hidden).toBe(true);
  });

  it('shows representative of collapsed cluster with badge', () => {
    const cm = makeClusterMap();
    const collapsed = new Set([0]);
    const result = computeClusterNodeStyle('n1', { thingType: 'default', size: 5, label: 'Home' }, cm, collapsed, new Set(), null, false);
    expect(result.label).toBe('Home (2)');
    expect(result.size).toBe(7.5); // 5 * 1.5
    expect(result.zIndex).toBe(2);
  });

  it('applies selection highlight to expanded cluster node', () => {
    const cm = makeClusterMap();
    const result = computeClusterNodeStyle('n1', { thingType: 'default' }, cm, new Set(), new Set(), 'n1', false);
    expect(result.highlighted).toBe(true);
    expect(result.zIndex).toBe(10);
  });

  it('does not dim node not in clusterMap at all', () => {
    const cm = makeClusterMap();
    const result = computeClusterNodeStyle('unknown', { thingType: 'default', color: '#ff0000' }, cm, new Set(), new Set(), null, false);
    // undefined clusterIndex → unclustered
    expect(result.color).toBe('#27272a');
  });

  it('uses fallback size when data.size is missing (collapsed cluster rep)', () => {
    const cm = makeClusterMap();
    const collapsed = new Set([0]);
    const result = computeClusterNodeStyle('n1', { thingType: 'default', label: 'X' }, cm, collapsed, new Set(), null, false);
    // Fallback size: (5) * 1.5 = 7.5
    expect(result.size).toBe(7.5);
    expect(result.label).toBe('X (2)');
  });

  it('uses empty string when data.label is missing (collapsed cluster rep)', () => {
    const cm = makeClusterMap();
    const collapsed = new Set([0]);
    const result = computeClusterNodeStyle('n1', { thingType: 'default', size: 5 }, cm, collapsed, new Set(), null, false);
    expect(result.label).toBe(' (2)');
  });

});
