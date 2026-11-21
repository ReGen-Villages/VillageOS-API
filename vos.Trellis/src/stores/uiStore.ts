import { create } from 'zustand';
import type { PredicateStatistics, ClusterMap } from '../utils/predicateCluster';
import { FLASH_DEFAULTS, LAYOUT_DEFAULTS } from '../utils/guiSettings';
import type { FlashSettings, LayoutSettings } from '../utils/guiSettings';
import type { SortOrder } from '../utils/typeFilter';
import { toggled } from '../utils/toggled';

const PANEL_WIDTH_KEY = 'vos-panel-width';
const DEFAULT_PANEL_WIDTH = 320;
const MIN_PANEL_WIDTH = 240;
const MAX_PANEL_WIDTH = 1600;

const HIDDEN_TYPES_KEY_PREFIX = 'vos-hidden-types:';
const TYPE_SORT_KEY_PREFIX = 'vos-type-sort:';
const DEFAULT_TYPE_SORT: SortOrder = 'count-desc';
const VALID_TYPE_SORTS: readonly SortOrder[] = ['count-desc', 'count-asc', 'name-asc', 'name-desc'];

/** Kept per model, so two models do not share their filter. */
function loadHiddenTypeIds(modelId: string | null): Set<string> {
  if (!modelId) return new Set<string>();
  const raw = localStorage.getItem(HIDDEN_TYPES_KEY_PREFIX + modelId);
  if (!raw) return new Set<string>();
  try {
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return new Set<string>();
    return new Set<string>(parsed.filter((x): x is string => typeof x === 'string'));
  } catch {
    return new Set<string>();
  }
}

function persistHiddenTypeIds(modelId: string | null, ids: Set<string>): void {
  if (!modelId) return;
  if (ids.size === 0) {
    localStorage.removeItem(HIDDEN_TYPES_KEY_PREFIX + modelId);
  } else {
    localStorage.setItem(HIDDEN_TYPES_KEY_PREFIX + modelId, JSON.stringify([...ids]));
  }
}

/** Kept per model in the browser's storage, so the chosen order lasts across reloads; a missing or unknown
 *  value falls back to largest first. */
function loadTypeSort(modelId: string | null): SortOrder {
  if (!modelId) return DEFAULT_TYPE_SORT;
  const raw = localStorage.getItem(TYPE_SORT_KEY_PREFIX + modelId);
  if (raw && (VALID_TYPE_SORTS as readonly string[]).includes(raw)) {
    return raw as SortOrder;
  }
  return DEFAULT_TYPE_SORT;
}

function persistTypeSort(modelId: string | null, order: SortOrder): void {
  if (!modelId) return;
  if (order === DEFAULT_TYPE_SORT) {
    localStorage.removeItem(TYPE_SORT_KEY_PREFIX + modelId);
  } else {
    localStorage.setItem(TYPE_SORT_KEY_PREFIX + modelId, order);
  }
}

function loadPanelWidth(): number {
  const stored = localStorage.getItem(PANEL_WIDTH_KEY);
  if (stored) {
    const n = parseInt(stored, 10);
    if (!isNaN(n) && n >= MIN_PANEL_WIDTH && n <= MAX_PANEL_WIDTH) return n;
  }
  return DEFAULT_PANEL_WIDTH;
}

interface UiState {
  selectedNodeId: string | null;
  selectedEdgeId: string | null;
  hoveredNodeId: string | null;
  detailPanelWidth: number;
  selectNode: (id: string | null) => void;
  selectEdge: (id: string | null) => void;
  setHoveredNodeId: (id: string | null) => void;
  setDetailPanelWidth: (width: number) => void;

  // Moved by useModelData on each StatesChanged event, so detail panels read ranges again without a
  // subscription of their own. A panel showing one Thing watches the total; a figure made of one state
  // watches that state's count, so it reads again only when that state moves. One entry per declared state.
  statesVersion: number;
  stateVersions: Record<string, number>;
  statesMoved: (states: string[]) => void;

  // Type Thing ids currently hidden; any target of an `is` relationship counts as a type. The Graph and
  // Model pages read the same set so the two views agree, and it is kept per model in localStorage.
  currentModelId: string | null;
  hiddenTypeIds: Set<string>;
  setCurrentModelId: (modelId: string | null) => void;
  setHiddenTypeIds: (ids: Set<string>) => void;
  clearHiddenTypeIds: () => void;

  typeSortOrder: SortOrder;
  setTypeSortOrder: (order: SortOrder) => void;

  // Predicate ids whose edges are hidden; an empty set shows every edge. Independent of
  // activePredicateIds, which drives clustering from the radial menu.
  hiddenPredicateIds: Set<string>;
  toggleHiddenPredicate: (predicateId: string) => void;
  setHiddenPredicateIds: (ids: Set<string>) => void;
  clearHiddenPredicateIds: () => void;

  activePredicateIds: Set<string>;
  clusterMap: ClusterMap | null;
  predicateStatistics: PredicateStatistics[];
  collapsedClusters: Set<number>;
  expandedNodes: Set<string>;
  radialMenuOpen: boolean;
  radialMenuPosition: { x: number; y: number } | null;
  expandedLogicalParents: Set<string>;
  semanticZoomEnabled: boolean;
  toggleLogicalExpansion: (geoNodeId: string) => void;
  expandLogicalParent: (id: string) => void;
  clearLogicalExpansions: () => void;
  setSemanticZoomEnabled: (enabled: boolean) => void;

  nodeContextMenuOpen: boolean;
  nodeContextMenuPosition: { x: number; y: number } | null;
  nodeContextMenuNodeId: string | null;
  openNodeContextMenu: (options: { nodeId: string; position: { x: number; y: number } }) => void;
  closeNodeContextMenu: () => void;

  isLayoutFrozen: boolean;
  toggleLayoutFrozen: () => void;

  isSpreadActive: boolean;
  toggleSpreadActive: () => void;

  layoutSettings: LayoutSettings;
  setLayoutSettings: (settings: LayoutSettings) => void;

  predicateColors: Record<string, string>;
  setPredicateColors: (colors: Record<string, string>) => void;

  flashSettings: FlashSettings;
  flashingNodeIds: Set<string>;
  flashingEdgeIds: Set<string>;
  setFlashSettings: (settings: FlashSettings) => void;
  addFlashNode: (id: string) => void;
  removeFlashNode: (id: string) => void;
  addFlashEdge: (id: string) => void;
  removeFlashEdge: (id: string) => void;

  togglePredicateId: (id: string) => void;
  clearPredicateIds: () => void;
  setClusterMap: (map: ClusterMap | null) => void;
  setPredicateStatistics: (statistics: PredicateStatistics[]) => void;
  toggleClusterCollapsed: (clusterIndex: number) => void;
  toggleNodeExpanded: (nodeId: string) => void;
  openRadialMenu: (position: { x: number; y: number }) => void;
  closeRadialMenu: () => void;
}

export { MIN_PANEL_WIDTH, MAX_PANEL_WIDTH };

export const useUiStore = create<UiState>((set) => ({
  selectedNodeId: null,
  selectedEdgeId: null,
  hoveredNodeId: null,
  detailPanelWidth: loadPanelWidth(),
  selectNode: (id) => set({ selectedNodeId: id, selectedEdgeId: null }),
  selectEdge: (id) => set({ selectedEdgeId: id, selectedNodeId: null }),
  setHoveredNodeId: (id) => set({ hoveredNodeId: id }),
  setDetailPanelWidth: (width) => {
    const clamped = Math.max(MIN_PANEL_WIDTH, Math.min(MAX_PANEL_WIDTH, width));
    localStorage.setItem(PANEL_WIDTH_KEY, String(clamped));
    set({ detailPanelWidth: clamped });
  },

  statesVersion: 0,
  stateVersions: {},
  statesMoved: (states) =>
    set((s) => {
      const moved = { ...s.stateVersions };
      for (const state of states) moved[state] = (moved[state] ?? 0) + 1;
      return { statesVersion: s.statesVersion + 1, stateVersions: moved };
    }),

  currentModelId: null,
  hiddenTypeIds: new Set<string>(),
  typeSortOrder: DEFAULT_TYPE_SORT,
  setCurrentModelId: (modelId) => set(() => ({
    currentModelId: modelId,
    hiddenTypeIds: loadHiddenTypeIds(modelId),
    typeSortOrder: loadTypeSort(modelId),
  })),
  setTypeSortOrder: (order) => set((s) => {
    persistTypeSort(s.currentModelId, order);
    return { typeSortOrder: order };
  }),
  setHiddenTypeIds: (ids) => set((s) => {
    persistHiddenTypeIds(s.currentModelId, ids);
    return { hiddenTypeIds: new Set(ids) };
  }),
  clearHiddenTypeIds: () => set((s) => {
    persistHiddenTypeIds(s.currentModelId, new Set<string>());
    return { hiddenTypeIds: new Set<string>() };
  }),

  hiddenPredicateIds: new Set<string>(),
  toggleHiddenPredicate: (predicateId) => set((s) => ({ hiddenPredicateIds: toggled(s.hiddenPredicateIds, predicateId) })),
  setHiddenPredicateIds: (ids) => set(() => ({
    hiddenPredicateIds: new Set(ids),
  })),
  clearHiddenPredicateIds: () => set(() => ({
    hiddenPredicateIds: new Set<string>(),
  })),

  activePredicateIds: new Set<string>(),
  clusterMap: null,
  predicateStatistics: [],
  collapsedClusters: new Set<number>(),
  expandedNodes: new Set<string>(),
  radialMenuOpen: false,
  radialMenuPosition: null,
  expandedLogicalParents: new Set<string>(),
  semanticZoomEnabled: true,

  toggleLogicalExpansion: (geoNodeId) =>
    set((state) => ({ expandedLogicalParents: toggled(state.expandedLogicalParents, geoNodeId) })),

  expandLogicalParent: (id) =>
    set((state) => {
      if (state.expandedLogicalParents.has(id)) return {};
      const next = new Set(state.expandedLogicalParents);
      next.add(id);
      return { expandedLogicalParents: next };
    }),

  clearLogicalExpansions: () => set({ expandedLogicalParents: new Set<string>() }),

  setSemanticZoomEnabled: (enabled) => set({ semanticZoomEnabled: enabled }),

  nodeContextMenuOpen: false,
  nodeContextMenuPosition: null,
  nodeContextMenuNodeId: null,
  openNodeContextMenu: ({ nodeId, position }) =>
    set({ nodeContextMenuOpen: true, nodeContextMenuPosition: position, nodeContextMenuNodeId: nodeId, radialMenuOpen: false, radialMenuPosition: null }),
  closeNodeContextMenu: () =>
    set({ nodeContextMenuOpen: false, nodeContextMenuPosition: null, nodeContextMenuNodeId: null }),

  isLayoutFrozen: false,
  toggleLayoutFrozen: () => set((s) => ({ isLayoutFrozen: !s.isLayoutFrozen })),

  isSpreadActive: false,
  toggleSpreadActive: () => set((s) => ({ isSpreadActive: !s.isSpreadActive })),

  layoutSettings: { ...LAYOUT_DEFAULTS },
  setLayoutSettings: (settings) => set({ layoutSettings: settings }),

  predicateColors: {},
  setPredicateColors: (colors) => set({ predicateColors: colors }),

  flashSettings: { ...FLASH_DEFAULTS },
  flashingNodeIds: new Set<string>(),
  flashingEdgeIds: new Set<string>(),
  setFlashSettings: (settings) => set({ flashSettings: settings }),
  addFlashNode: (id) =>
    set((s) => {
      const next = new Set(s.flashingNodeIds);
      next.add(id);
      return { flashingNodeIds: next };
    }),
  removeFlashNode: (id) =>
    set((s) => {
      if (!s.flashingNodeIds.has(id)) return {};
      const next = new Set(s.flashingNodeIds);
      next.delete(id);
      return { flashingNodeIds: next };
    }),
  addFlashEdge: (id) =>
    set((s) => {
      const next = new Set(s.flashingEdgeIds);
      next.add(id);
      return { flashingEdgeIds: next };
    }),
  removeFlashEdge: (id) =>
    set((s) => {
      if (!s.flashingEdgeIds.has(id)) return {};
      const next = new Set(s.flashingEdgeIds);
      next.delete(id);
      return { flashingEdgeIds: next };
    }),

  togglePredicateId: (id) =>
    set((state) => ({
      activePredicateIds: toggled(state.activePredicateIds, id),
      collapsedClusters: new Set<number>(),
      expandedNodes: new Set<string>(),
      clusterMap: null, // recomputed by ClusterComputer
    })),

  clearPredicateIds: () =>
    set({
      activePredicateIds: new Set<string>(),
      collapsedClusters: new Set<number>(),
      expandedNodes: new Set<string>(),
      clusterMap: null,
    }),

  setClusterMap: (map) => set({ clusterMap: map }),
  setPredicateStatistics: (statistics) => set({ predicateStatistics: statistics }),

  toggleClusterCollapsed: (clusterIndex) =>
    set((state) => ({ collapsedClusters: toggled(state.collapsedClusters, clusterIndex) })),

  toggleNodeExpanded: (nodeId) =>
    set((state) => ({ expandedNodes: toggled(state.expandedNodes, nodeId) })),

  openRadialMenu: (position) => set({ radialMenuOpen: true, radialMenuPosition: position, nodeContextMenuOpen: false, nodeContextMenuPosition: null, nodeContextMenuNodeId: null }),
  closeRadialMenu: () => set({ radialMenuOpen: false, radialMenuPosition: null }),
}));
