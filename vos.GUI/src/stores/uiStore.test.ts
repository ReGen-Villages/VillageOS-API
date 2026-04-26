import { describe, it, expect, beforeEach } from 'vitest';


// Ensure localStorage is available (jsdom may not have it in all configs)
if (!globalThis.localStorage || typeof globalThis.localStorage.getItem !== 'function') {
  const store: Record<string, string> = {};
  Object.defineProperty(globalThis, 'localStorage', {
    value: {
      getItem: (key: string) => store[key] ?? null,
      setItem: (key: string, val: string) => { store[key] = val; },
      removeItem: (key: string) => { delete store[key]; },
      clear: () => { for (const k of Object.keys(store)) delete store[k]; },
      get length() { return Object.keys(store).length; },
      key: (i: number) => Object.keys(store)[i] ?? null,
    },
    writable: true,
    configurable: true,
  });
}

// Import after localStorage is available
const { useUiStore } = await import('./uiStore');

// Reset Zustand store state before each test
beforeEach(() => {
  useUiStore.setState({
    selectedNodeId: null,
    selectedEdgeId: null,
    hoveredNodeId: null,
    activePredicateIds: new Set<string>(),
    clusterMap: null,
    predicateStats: [],
    collapsedClusters: new Set<number>(),
    expandedNodes: new Set<string>(),
    radialMenuOpen: false,
    radialMenuPosition: null,
    expandedLogicalParents: new Set<string>(),
    semanticZoomEnabled: true,
    isLayoutFrozen: false,
    nodeContextMenuOpen: false,
    nodeContextMenuPosition: null,
    nodeContextMenuNodeId: null,
  });
});

describe('selectNode', () => {
  it('sets selectedNodeId', () => {
    useUiStore.getState().selectNode('n1');
    expect(useUiStore.getState().selectedNodeId).toBe('n1');
  });

  it('clears selectedEdgeId when selecting a node', () => {
    useUiStore.setState({ selectedEdgeId: 'e1' });
    useUiStore.getState().selectNode('n1');
    expect(useUiStore.getState().selectedEdgeId).toBeNull();
  });

  it('clears selectedNodeId when deselecting (null)', () => {
    useUiStore.getState().selectNode('n1');
    useUiStore.getState().selectNode(null);
    expect(useUiStore.getState().selectedNodeId).toBeNull();
  });
});

describe('selectEdge', () => {
  it('sets selectedEdgeId', () => {
    useUiStore.getState().selectEdge('e1');
    expect(useUiStore.getState().selectedEdgeId).toBe('e1');
  });

  it('clears selectedNodeId when selecting an edge', () => {
    useUiStore.setState({ selectedNodeId: 'n1' });
    useUiStore.getState().selectEdge('e1');
    expect(useUiStore.getState().selectedNodeId).toBeNull();
  });
});

describe('togglePredicateId', () => {
  it('adds a predicate id on first toggle', () => {
    useUiStore.getState().togglePredicateId('p1');
    expect(useUiStore.getState().activePredicateIds.has('p1')).toBe(true);
  });

  it('removes predicate id on second toggle', () => {
    useUiStore.getState().togglePredicateId('p1');
    useUiStore.getState().togglePredicateId('p1');
    expect(useUiStore.getState().activePredicateIds.has('p1')).toBe(false);
  });

  it('resets clusterMap and expansion state', () => {
    useUiStore.setState({
      clusterMap: { nodeCluster: new Map(), clusters: [], representatives: new Map() },
      collapsedClusters: new Set([0]),
      expandedNodes: new Set(['n1']),
    });
    useUiStore.getState().togglePredicateId('p1');
    const state = useUiStore.getState();
    expect(state.clusterMap).toBeNull();
    expect(state.collapsedClusters.size).toBe(0);
    expect(state.expandedNodes.size).toBe(0);
  });

});

describe('toggleClusterCollapsed', () => {
  it('adds cluster index on first toggle', () => {
    useUiStore.getState().toggleClusterCollapsed(0);
    expect(useUiStore.getState().collapsedClusters.has(0)).toBe(true);
  });

  it('removes cluster index on second toggle', () => {
    useUiStore.getState().toggleClusterCollapsed(0);
    useUiStore.getState().toggleClusterCollapsed(0);
    expect(useUiStore.getState().collapsedClusters.has(0)).toBe(false);
  });
});

describe('toggleNodeExpanded', () => {
  it('adds node on first toggle', () => {
    useUiStore.getState().toggleNodeExpanded('n1');
    expect(useUiStore.getState().expandedNodes.has('n1')).toBe(true);
  });

  it('removes node on second toggle', () => {
    useUiStore.getState().toggleNodeExpanded('n1');
    useUiStore.getState().toggleNodeExpanded('n1');
    expect(useUiStore.getState().expandedNodes.has('n1')).toBe(false);
  });
});

describe('toggleLogicalExpansion', () => {
  it('adds geo node on first toggle', () => {
    useUiStore.getState().toggleLogicalExpansion('g1');
    expect(useUiStore.getState().expandedLogicalParents.has('g1')).toBe(true);
  });

  it('removes geo node on second toggle', () => {
    useUiStore.getState().toggleLogicalExpansion('g1');
    useUiStore.getState().toggleLogicalExpansion('g1');
    expect(useUiStore.getState().expandedLogicalParents.has('g1')).toBe(false);
  });
});

describe('toggleLayoutFrozen', () => {
  it('flips isLayoutFrozen', () => {
    expect(useUiStore.getState().isLayoutFrozen).toBe(false);
    useUiStore.getState().toggleLayoutFrozen();
    expect(useUiStore.getState().isLayoutFrozen).toBe(true);
    useUiStore.getState().toggleLayoutFrozen();
    expect(useUiStore.getState().isLayoutFrozen).toBe(false);
  });
});

describe('setDetailPanelWidth', () => {
  it('clamps to minimum', () => {
    useUiStore.getState().setDetailPanelWidth(100);
    expect(useUiStore.getState().detailPanelWidth).toBe(240);
  });

  it('clamps to maximum', () => {
    useUiStore.getState().setDetailPanelWidth(2000);
    expect(useUiStore.getState().detailPanelWidth).toBe(1600);
  });

  it('accepts values within range', () => {
    useUiStore.getState().setDetailPanelWidth(400);
    expect(useUiStore.getState().detailPanelWidth).toBe(400);
  });
});

describe('clearPredicateIds', () => {
  it('resets all clustering state', () => {
    useUiStore.setState({
      activePredicateIds: new Set(['p1']),
      clusterMap: { nodeCluster: new Map(), clusters: [], representatives: new Map() },
      collapsedClusters: new Set([0]),
      expandedNodes: new Set(['n1']),
    });
    useUiStore.getState().clearPredicateIds();
    const state = useUiStore.getState();
    expect(state.activePredicateIds.size).toBe(0);
    expect(state.clusterMap).toBeNull();
    expect(state.collapsedClusters.size).toBe(0);
    expect(state.expandedNodes.size).toBe(0);
  });
});

describe('openNodeContextMenu', () => {
  it('sets context menu state and closes radial menu', () => {
    useUiStore.setState({ radialMenuOpen: true, radialMenuPosition: { x: 50, y: 50 } });
    useUiStore.getState().openNodeContextMenu({ nodeId: 'n1', position: { x: 100, y: 200 } });
    const state = useUiStore.getState();
    expect(state.nodeContextMenuOpen).toBe(true);
    expect(state.nodeContextMenuNodeId).toBe('n1');
    expect(state.nodeContextMenuPosition).toEqual({ x: 100, y: 200 });
    expect(state.radialMenuOpen).toBe(false);
    expect(state.radialMenuPosition).toBeNull();
  });
});

describe('closeNodeContextMenu', () => {
  it('resets all context menu state', () => {
    useUiStore.getState().openNodeContextMenu({ nodeId: 'n1', position: { x: 100, y: 200 } });
    useUiStore.getState().closeNodeContextMenu();
    const state = useUiStore.getState();
    expect(state.nodeContextMenuOpen).toBe(false);
    expect(state.nodeContextMenuNodeId).toBeNull();
    expect(state.nodeContextMenuPosition).toBeNull();
  });
});

describe('openRadialMenu', () => {
  it('closes node context menu when opening radial menu', () => {
    useUiStore.getState().openNodeContextMenu({ nodeId: 'n1', position: { x: 10, y: 20 } });
    useUiStore.getState().openRadialMenu({ x: 50, y: 50 });
    const state = useUiStore.getState();
    expect(state.radialMenuOpen).toBe(true);
    expect(state.nodeContextMenuOpen).toBe(false);
    expect(state.nodeContextMenuNodeId).toBeNull();
  });
});

describe('hiddenTypeIds — type filter (Feature #5362)', () => {
  beforeEach(() => {
    localStorage.clear();
    useUiStore.setState({
      currentModelId: null,
      hiddenTypeIds: new Set<string>(),
    });
  });

  it('starts empty when no model is selected', () => {
    expect(useUiStore.getState().hiddenTypeIds.size).toBe(0);
  });

  it('toggleHiddenType adds and removes ids', () => {
    const { toggleHiddenType } = useUiStore.getState();
    toggleHiddenType('t-wall');
    expect(useUiStore.getState().hiddenTypeIds.has('t-wall')).toBe(true);
    toggleHiddenType('t-wall');
    expect(useUiStore.getState().hiddenTypeIds.has('t-wall')).toBe(false);
  });

  it('persists hidden ids per modelId', () => {
    const { setCurrentModelId, toggleHiddenType } = useUiStore.getState();
    setCurrentModelId('model-A');
    toggleHiddenType('t-wall');
    toggleHiddenType('t-door');

    // Switch to a different model — store should clear (different localStorage key)
    setCurrentModelId('model-B');
    expect(useUiStore.getState().hiddenTypeIds.size).toBe(0);

    // Switch back — persisted state restored
    setCurrentModelId('model-A');
    const restored = useUiStore.getState().hiddenTypeIds;
    expect(restored.has('t-wall')).toBe(true);
    expect(restored.has('t-door')).toBe(true);
  });

  it('clearHiddenTypeIds wipes the set and persists empty', () => {
    const { setCurrentModelId, toggleHiddenType, clearHiddenTypeIds } = useUiStore.getState();
    setCurrentModelId('m1');
    toggleHiddenType('t-x');
    expect(useUiStore.getState().hiddenTypeIds.size).toBe(1);
    clearHiddenTypeIds();
    expect(useUiStore.getState().hiddenTypeIds.size).toBe(0);
    // Re-load model — should still be empty
    useUiStore.getState().setCurrentModelId(null);
    useUiStore.getState().setCurrentModelId('m1');
    expect(useUiStore.getState().hiddenTypeIds.size).toBe(0);
  });

  it('setHiddenTypeIds replaces the entire set', () => {
    const { setCurrentModelId, setHiddenTypeIds } = useUiStore.getState();
    setCurrentModelId('m1');
    setHiddenTypeIds(new Set(['a', 'b', 'c']));
    expect([...useUiStore.getState().hiddenTypeIds].sort()).toEqual(['a', 'b', 'c']);
  });

  it('does not persist when no model is selected', () => {
    // Without a modelId, toggles still update in-memory state but skip
    // localStorage so we don't pollute a global key.
    useUiStore.setState({ currentModelId: null, hiddenTypeIds: new Set<string>() });
    useUiStore.getState().toggleHiddenType('t-wall');
    // No localStorage entry should exist for an unkeyed model
    const allKeys: string[] = [];
    for (let i = 0; i < localStorage.length; i++) {
      const k = localStorage.key(i);
      if (k && k.startsWith('vos-hidden-types:')) allKeys.push(k);
    }
    expect(allKeys).toEqual([]);
  });
});
