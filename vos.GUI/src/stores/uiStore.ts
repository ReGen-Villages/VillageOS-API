import { create } from 'zustand';
import type { PredicateStats, ClusterMap } from '../utils/predicateCluster';
import { FLASH_DEFAULTS, LAYOUT_DEFAULTS } from '../utils/guiSettings';
import type { FlashSettings, LayoutSettings } from '../utils/guiSettings';

const PANEL_WIDTH_KEY = 'vos-panel-width';
const DEFAULT_PANEL_WIDTH = 320;
const MIN_PANEL_WIDTH = 240;
const MAX_PANEL_WIDTH = 1600;

const HIDDEN_TYPES_KEY_PREFIX = 'vos-hidden-types:';

/**
 * Feature #5362 — load the persisted set of hidden type Thing ids for a model.
 * Per-model so different models don't share filter state. Returns an empty
 * Set when no model is selected or no persisted state exists.
 */
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

function loadPanelWidth(): number {
  const stored = localStorage.getItem(PANEL_WIDTH_KEY);
  if (stored) {
    const n = parseInt(stored, 10);
    if (!isNaN(n) && n >= MIN_PANEL_WIDTH && n <= MAX_PANEL_WIDTH) return n;
  }
  return DEFAULT_PANEL_WIDTH;
}

interface UiState {
  // ── Selection & hover ─────────────────────────────────────────────
  selectedNodeId: string | null;
  selectedEdgeId: string | null;
  hoveredNodeId: string | null;
  detailPanelWidth: number;
  selectNode: (id: string | null) => void;
  selectEdge: (id: string | null) => void;
  setHoveredNodeId: (id: string | null) => void;
  setDetailPanelWidth: (width: number) => void;

  // ── States refresh counter ────────────────────────────────────────
  // Bumped by useModelData on the broker's `StatesChanged` SignalR
  // event so detail panels re-fetch ranges without each page owning a
  // local subscription.
  statesVersion: number;
  bumpStatesVersion: () => void;


  // ── Type filter (Feature #5362) ───────────────────────────────────
  // Set of type Thing ids currently hidden. Domain-agnostic — any Thing
  // that is the target of an `is` relationship is a "type" for purposes
  // of this filter. Both the Graph page (Sigma) and the Model page
  // (Fragments) consume this same set so the two views stay in sync.
  // Persisted per-model via localStorage so different domains keep
  // separate filter state across reloads.
  currentModelId: string | null;
  hiddenTypeIds: Set<string>;
  setCurrentModelId: (modelId: string | null) => void;
  toggleHiddenType: (typeId: string) => void;
  setHiddenTypeIds: (ids: Set<string>) => void;
  clearHiddenTypeIds: () => void;

  // ── Predicate clustering ───────────────────────────────────────────
  activePredicateIds: Set<string>;
  clusterMap: ClusterMap | null;
  predicateStats: PredicateStats[];
  collapsedClusters: Set<number>;
  expandedNodes: Set<string>;
  radialMenuOpen: boolean;
  radialMenuPosition: { x: number; y: number } | null;
  // ── Logical node expansion (Phase 3) ─────────────────────────────
  expandedLogicalParents: Set<string>;
  semanticZoomEnabled: boolean;
  toggleLogicalExpansion: (geoNodeId: string) => void;
  expandLogicalParent: (id: string) => void;
  clearLogicalExpansions: () => void;
  setSemanticZoomEnabled: (enabled: boolean) => void;

  // ── Node context menu ──────────────────────────────────────────
  nodeContextMenuOpen: boolean;
  nodeContextMenuPosition: { x: number; y: number } | null;
  nodeContextMenuNodeId: string | null;
  openNodeContextMenu: (opts: { nodeId: string; position: { x: number; y: number } }) => void;
  closeNodeContextMenu: () => void;

  // ── Layout freeze ────────────────────────────────────────────────
  isLayoutFrozen: boolean;
  toggleLayoutFrozen: () => void;

  // ── Spread mode ────────────────────────────────────────────────
  isSpreadActive: boolean;
  toggleSpreadActive: () => void;

  // ── Layout settings (from GUI Settings Thing) ────────────────────
  layoutSettings: LayoutSettings;
  setLayoutSettings: (settings: LayoutSettings) => void;

  // ── Predicate colors (from GUI_Settings PredicateColors) ────────
  predicateColors: Record<string, string>;
  setPredicateColors: (colors: Record<string, string>) => void;

  // ── Flash effects (property mutation pulse) ─────────────────────
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
  setPredicateIds: (ids: Set<string>) => void;
  setClusterMap: (map: ClusterMap | null) => void;
  setPredicateStats: (stats: PredicateStats[]) => void;
  toggleClusterCollapsed: (clusterIndex: number) => void;
  toggleNodeExpanded: (nodeId: string) => void;
  openRadialMenu: (position: { x: number; y: number }) => void;
  closeRadialMenu: () => void;
}

export { MIN_PANEL_WIDTH, MAX_PANEL_WIDTH };

export const useUiStore = create<UiState>((set) => ({
  // ── Selection & hover ────────────────────────────────────────────────
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

  // ── States refresh counter ────────────────────────────────────────
  statesVersion: 0,
  bumpStatesVersion: () => set((s) => ({ statesVersion: s.statesVersion + 1 })),

  // ── Type filter (Feature #5362) ───────────────────────────────────
  currentModelId: null,
  hiddenTypeIds: new Set<string>(),
  setCurrentModelId: (modelId) => set(() => ({
    currentModelId: modelId,
    // Re-load persisted filter state for the new model
    hiddenTypeIds: loadHiddenTypeIds(modelId),
  })),
  toggleHiddenType: (typeId) => set((s) => {
    const next = new Set(s.hiddenTypeIds);
    if (next.has(typeId)) next.delete(typeId);
    else next.add(typeId);
    persistHiddenTypeIds(s.currentModelId, next);
    return { hiddenTypeIds: next };
  }),
  setHiddenTypeIds: (ids) => set((s) => {
    persistHiddenTypeIds(s.currentModelId, ids);
    return { hiddenTypeIds: new Set(ids) };
  }),
  clearHiddenTypeIds: () => set((s) => {
    persistHiddenTypeIds(s.currentModelId, new Set<string>());
    return { hiddenTypeIds: new Set<string>() };
  }),

  // ── Predicate clustering ─────────────────────────────────────────────
  activePredicateIds: new Set<string>(),
  clusterMap: null,
  predicateStats: [],
  collapsedClusters: new Set<number>(),
  expandedNodes: new Set<string>(),
  radialMenuOpen: false,
  radialMenuPosition: null,
  // ── Logical node expansion (Phase 3) ────────────────────────────────
  expandedLogicalParents: new Set<string>(),
  semanticZoomEnabled: true,

  toggleLogicalExpansion: (geoNodeId) =>
    set((state) => {
      const next = new Set(state.expandedLogicalParents);
      if (next.has(geoNodeId)) next.delete(geoNodeId);
      else next.add(geoNodeId);
      return { expandedLogicalParents: next };
    }),

  expandLogicalParent: (id) =>
    set((state) => {
      if (state.expandedLogicalParents.has(id)) return {};
      const next = new Set(state.expandedLogicalParents);
      next.add(id);
      return { expandedLogicalParents: next };
    }),

  clearLogicalExpansions: () => set({ expandedLogicalParents: new Set<string>() }),

  setSemanticZoomEnabled: (enabled) => set({ semanticZoomEnabled: enabled }),

  // ── Node context menu ────────────────────────────────────────────────
  nodeContextMenuOpen: false,
  nodeContextMenuPosition: null,
  nodeContextMenuNodeId: null,
  openNodeContextMenu: ({ nodeId, position }) =>
    set({ nodeContextMenuOpen: true, nodeContextMenuPosition: position, nodeContextMenuNodeId: nodeId, radialMenuOpen: false, radialMenuPosition: null }),
  closeNodeContextMenu: () =>
    set({ nodeContextMenuOpen: false, nodeContextMenuPosition: null, nodeContextMenuNodeId: null }),

  // ── Layout freeze ──────────────────────────────────────────────────
  isLayoutFrozen: false,
  toggleLayoutFrozen: () => set((s) => ({ isLayoutFrozen: !s.isLayoutFrozen })),

  // ── Spread mode ──────────────────────────────────────────────────
  isSpreadActive: false,
  toggleSpreadActive: () => set((s) => ({ isSpreadActive: !s.isSpreadActive })),

  // ── Layout settings (from GUI Settings Thing) ──────────────────────
  layoutSettings: { ...LAYOUT_DEFAULTS },
  setLayoutSettings: (settings) => set({ layoutSettings: settings }),

  // ── Predicate colors (from GUI_Settings PredicateColors) ─────────────
  predicateColors: {},
  setPredicateColors: (colors) => set({ predicateColors: colors }),

  // ── Flash effects (property mutation pulse) ─────────────────────────
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
    set((state) => {
      const next = new Set(state.activePredicateIds);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return {
        activePredicateIds: next,
        collapsedClusters: new Set<number>(),
        expandedNodes: new Set<string>(),
        clusterMap: null, // recomputed by ClusterComputer
      };
    }),

  clearPredicateIds: () =>
    set({
      activePredicateIds: new Set<string>(),
      collapsedClusters: new Set<number>(),
      expandedNodes: new Set<string>(),
      clusterMap: null,
    }),

  setPredicateIds: (ids) =>
    set({
      activePredicateIds: ids,
      collapsedClusters: new Set<number>(),
      expandedNodes: new Set<string>(),
      clusterMap: null,
    }),

  setClusterMap: (map) => set({ clusterMap: map }),
  setPredicateStats: (stats) => set({ predicateStats: stats }),

  toggleClusterCollapsed: (clusterIndex) =>
    set((state) => {
      const next = new Set(state.collapsedClusters);
      if (next.has(clusterIndex)) next.delete(clusterIndex);
      else next.add(clusterIndex);
      return { collapsedClusters: next };
    }),

  toggleNodeExpanded: (nodeId) =>
    set((state) => {
      const next = new Set(state.expandedNodes);
      if (next.has(nodeId)) next.delete(nodeId);
      else next.add(nodeId);
      return { expandedNodes: next };
    }),

  openRadialMenu: (position) => set({ radialMenuOpen: true, radialMenuPosition: position, nodeContextMenuOpen: false, nodeContextMenuPosition: null, nodeContextMenuNodeId: null }),
  closeRadialMenu: () => set({ radialMenuOpen: false, radialMenuPosition: null }),
}));
