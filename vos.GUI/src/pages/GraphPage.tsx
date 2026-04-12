import { useEffect, useState, useCallback, useMemo, useRef } from 'react';
import { SigmaCanvas } from '../components/graph/SigmaCanvas';
import { NodeDetailPanel } from '../components/panels/NodeDetailPanel';
import { EdgeDetailPanel } from '../components/panels/EdgeDetailPanel';
import { ResizablePanel } from '../components/panels/ResizablePanel';
import { RadialPredicateMenu } from '../components/graph/RadialPredicateMenu';
import { NodeContextMenu } from '../components/graph/NodeContextMenu';
import { ErrorBoundary } from '../components/common/ErrorBoundary';
import { useUiStore } from '../stores/uiStore';
import { useModelStore } from '../stores/modelStore';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { useSignalR } from '../hooks/useSignalR';
import { useFlashTimer } from '../hooks/useFlashTimer';
import { toast } from '../components/common/Toast';
import { ConfirmDialog } from '../components/common/ConfirmDialog';
import type { VosThing, VosRelationship } from '../types/vos';
import { filterGraph } from '../utils/searchFilter';
import { isGraphAffectingProperty, applyThingPropertyUpdate, applyRelationshipPropertyUpdate, isVisibleRelationship } from '../utils/propertyUpdates';
import { isContainmentPredicate } from '../utils/nodeVisibility';
import { useAuth } from '../hooks/useAuth';
import { Search, X, LogOut, ArrowLeftRight, Plus, Loader2, Layers } from 'lucide-react';

export function GraphPage() {
  const { logout, switchModel, modelName } = useAuth();
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const setThings = useModelStore((s) => s.setThings);
  const setRelationships = useModelStore((s) => s.setRelationships);
  const updateThings = useModelStore((s) => s.updateThings);
  const [searchQuery, setSearchQuery] = useState('');
  const [caseSensitive, setCaseSensitive] = useState(false);
  const [exactMatch, setExactMatch] = useState(false);
  const [useRegex, setUseRegex] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<{ type: 'thing' | 'relationship'; id: string; name: string } | null>(null);
  const [statesVersion, setStatesVersion] = useState(0);
  const [showCreateThing, setShowCreateThing] = useState(false);
  const [newThingName, setNewThingName] = useState('');
  const [creatingThing, setCreatingThing] = useState(false);

  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const selectedEdgeId = useUiStore((s) => s.selectedEdgeId);
  const selectNode = useUiStore((s) => s.selectNode);
  const selectEdge = useUiStore((s) => s.selectEdge);
  const { on } = useSignalR();
  const { triggerFlashNode, triggerFlashEdge } = useFlashTimer();

  // Ref so SignalR callbacks can access current relationships without stale closures
  const relationshipsRef = useRef(relationships);
  relationshipsRef.current = relationships;

  const [detailThing, setDetailThing] = useState<VosThing | null>(null);
  const [detailRelationship, setDetailRelationship] = useState<VosRelationship | null>(null);
  const thingMap = useMemo(() => new Map(things.map((t) => [t.Id, t])), [things]);
  const thingMapRef = useRef(thingMap);
  thingMapRef.current = thingMap;
  const hasGeoNodes = useMemo(() => things.some((t) =>
    (typeof t.Properties?.latitude === 'number' && typeof t.Properties?.longitude === 'number') ||
    t.Properties?.geometry != null,
  ), [things]);

  // Clear stale predicate/map state on mount so a fresh view starts clean.
  // The mapEnabled reset is critical: MaplibreLayer needs a false→true
  // transition to create a fresh binding. Without it, the binding may be
  // created before Sigma has processed the graph, and MapLibre never
  // fetches tiles.
  useEffect(() => {
    useUiStore.getState().clearPredicateIds();
    useUiStore.getState().setMapEnabled(false);
  }, []);

  // After mount reset, derive mapEnabled from current data.
  useEffect(() => {
    if (things.length > 0 && hasGeoNodes) {
      useUiStore.getState().setMapEnabled(true);
    }
  }, [things, hasGeoNodes]);

  // Fetch full thing detail (including inherited properties) when a node is selected.
  // Only re-fetches on node selection change — live property updates come via SignalR / setDetailThing.
  useEffect(() => {
    if (!selectedNodeId) {
      setDetailThing(null);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const thing = await thingApi.get(selectedNodeId);
        if (cancelled) return;
        setDetailThing(thing);
      } catch {
        if (!cancelled) {
          setDetailThing(thingMapRef.current.get(selectedNodeId) || null);
        }
      }
    })();
    return () => { cancelled = true; };
  }, [selectedNodeId]);

  // Sync detailRelationship when edge selection changes.
  // Reads from ref to avoid depending on the relationships array.
  useEffect(() => {
    if (!selectedEdgeId) {
      setDetailRelationship(null);
      return;
    }
    setDetailRelationship(relationshipsRef.current.find((r) => r.Id === selectedEdgeId) || null);
  }, [selectedEdgeId]);

  const syncDetailPanels = useCallback((t: VosThing[], r: VosRelationship[]) => {
    const selNode = useUiStore.getState().selectedNodeId;
    if (selNode) {
      const fresh = t.find((thing) => thing.Id === selNode);
      if (fresh) {
        setDetailThing((prev) => prev ? { ...prev, Properties: fresh.Properties } : fresh);
      }
    }
    const selEdge = useUiStore.getState().selectedEdgeId;
    if (selEdge) setDetailRelationship(r.find((rel) => rel.Id === selEdge) || null);
  }, []);

  // Full reload (used by SignalR callbacks and after mutations)
  const loadData = useCallback(async () => {
    try {
      const [t, r] = await Promise.all([thingApi.getAll(), relationshipApi.getAll()]);
      setThings(t);
      setRelationships(r);
      syncDetailPanels(t, r);
    } catch (err) {
      toast.error('Failed to load model');
    }
  }, [syncDetailPanels, setThings, setRelationships]);

  // Model data is loaded at app level (useModelLoader in App.tsx).
  // GraphPage just consumes the store data.

  // SignalR live updates
  useEffect(() => {
    const unsubs = [
      on('ThingCreated', () => loadData()),
      on('ThingDeleted', () => loadData()),
      on('RelationshipCreated', () => loadData()),
      on('RelationshipDeleted', () => loadData()),
      on('PropertyChanged', (...args: unknown[]) => {
        const thingId = args[0] as string;
        const propertyPath = args[1] as string | undefined;
        const newValue = args[2] as unknown;
        if (thingId && propertyPath !== undefined) {
          triggerFlashNode(thingId);
          // Only rebuild things array for properties that affect graph rendering (geometry).
          // All other property changes are detail-panel concerns only — skip O(n) array rebuild.
          if (isGraphAffectingProperty(propertyPath)) {
            updateThings((prev) => prev.map((t) =>
              t.Id === thingId ? applyThingPropertyUpdate(t, propertyPath, newValue) : t,
            ));
          }
          setDetailThing((prev) =>
            prev && prev.Id === thingId ? applyThingPropertyUpdate(prev, propertyPath, newValue) : prev,
          );
        }
      }),
      on('RelationshipPropertyChanged', (...args: unknown[]) => {
        const relId = args[0] as string;
        const propertyName = args[1] as string | undefined;
        const newValue = args[2] as unknown;
        if (relId && propertyName !== undefined) {
          triggerFlashEdge(relId);
          // Always update edge detail panel — O(1)
          setDetailRelationship((prev) =>
            prev && prev.Id === relId ? applyRelationshipPropertyUpdate(prev, propertyName, newValue) : prev,
          );
          // Only rebuild relationships array if this rel is visible in the node detail panel
          const selNode = useUiStore.getState().selectedNodeId;
          if (isVisibleRelationship(relId, selNode, relationshipsRef.current)) {
            useModelStore.getState().updateRelationships((prev) => prev.map((r) =>
              r.Id === relId ? applyRelationshipPropertyUpdate(r, propertyName, newValue) : r,
            ));
          }
        }
      }),
      on('ModelChanged', () => loadData()),
      on('ModelCleared', () => {
        useModelStore.getState().clear();
      }),
      on('StatesChanged', () => setStatesVersion((v) => v + 1)),
    ];
    return () => unsubs.forEach((u) => u());
  }, [on, loadData, triggerFlashNode, triggerFlashEdge, updateThings]);

  const handleDeleteThing = async () => {
    if (!deleteConfirm || deleteConfirm.type !== 'thing') return;
    try {
      await thingApi.remove(deleteConfirm.id);
      toast.success(`Deleted: ${deleteConfirm.name}`);
      selectNode(null);
      loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Delete failed');
    }
    setDeleteConfirm(null);
  };

  const handleDeleteRelationship = async () => {
    if (!deleteConfirm || deleteConfirm.type !== 'relationship') return;
    try {
      await relationshipApi.remove(deleteConfirm.id);
      toast.success('Relationship deleted');
      selectEdge(null);
      loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Delete failed');
    }
    setDeleteConfirm(null);
  };

  const handleDeleteProperty = async (thingId: string, propertyName: string) => {
    try {
      await thingApi.deleteProperty(thingId, propertyName);
      toast.success(`Deleted property: ${propertyName}`);
      loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Delete failed');
    }
  };

  const handleDeleteRelProperty = async (relationshipId: string, propertyName: string) => {
    try {
      await relationshipApi.deleteProperty(relationshipId, propertyName);
      toast.success(`Deleted property: ${propertyName}`);
      loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Delete failed');
    }
  };

  const handleCreateThing = async () => {
    if (!newThingName.trim() || creatingThing) return;
    setCreatingThing(true);
    try {
      await thingApi.create(newThingName.trim());
      toast.success(`Created thing: ${newThingName.trim()}`);
      setNewThingName('');
      setShowCreateThing(false);
      loadData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to create thing');
    } finally {
      setCreatingThing(false);
    }
  };

  const loadingPhase = useUiStore((s) => s.loadingPhase);

  // ── Map-mode graph reduction ─────────────────────────────────────────
  // When map is active, only load geo-relevant nodes into Sigma to avoid
  // overwhelming WebGL (8K nodes + 12K edges + 4 WebGL contexts → crash).
  // Keep: surface nodes, predicate things, type targets (via "is").
  const mapEnabled = useUiStore((s) => s.mapEnabled);
  const showAllThings = useUiStore((s) => s.showAllThings);
  const toggleShowAllThings = useUiStore((s) => s.toggleShowAllThings);
  const hideOrphanSites = useUiStore((s) => s.hideOrphanSites);
  const [togglingShowAll, setTogglingShowAll] = useState(false);

  const { graphThings, graphRelationships } = useMemo(() => {
    if (!mapEnabled) return { graphThings: things, graphRelationships: relationships };

    // Show all physical things (have geometry/footprint) instead of just surface things
    if (showAllThings) {
      const geoIds = new Set<string>();
      for (const t of things) {
        if (t.Properties?.geometry != null || t.Properties?.footprint != null) {
          geoIds.add(t.Id);
        }
      }
      // Also keep predicates + type targets for color classification
      const keepIds = new Set(geoIds);
      for (const r of relationships) keepIds.add(r.PredicateId);
      const thingNameMap = new Map(things.map((t) => [t.Id, t.Name]));
      for (const r of relationships) {
        const predName = thingNameMap.get(r.PredicateId)?.toLowerCase();
        if (predName === 'is' && geoIds.has(r.SubjectId)) {
          keepIds.add(r.TargetId);
        }
      }
      const gt = things.filter((t) => keepIds.has(t.Id));
      const gr = relationships.filter((r) => keepIds.has(r.SubjectId) && keepIds.has(r.TargetId));
      return { graphThings: gt, graphRelationships: gr };
    }

    // Surface things have __IsSurface=true (stamped by broker at seed load).
    // When hideOrphanSites is on (default), filter further to __IsMapSurfaceThing:
    // only things inside the primary IfcSite's spatial subtree, which the IFC
    // importer stamps directly during its native spatial walk. This hides
    // Revit template-default sites that come attached to imported family
    // instances but aren't part of the real project's spatial hierarchy.
    const surfaceIds = new Set<string>();
    for (const t of things) {
      const included = hideOrphanSites
        ? t.Properties?.__IsMapSurfaceThing === true
        : t.Properties?.__IsSurface === true;
      if (included) {
        surfaceIds.add(t.Id);
      }
    }

    // Also keep predicate things and type targets (for color classification).
    // Skip contains/aggregates neighbors — those are thousands of IFC sub-elements.
    const keepIds = new Set(surfaceIds);
    for (const r of relationships) keepIds.add(r.PredicateId);

    // Non-geo neighbors of surface nodes: include both endpoints of any
    // relationship touching a surface node, EXCEPT containment predicates
    // (thousands of IFC sub-elements that would overwhelm WebGL).
    // Containment predicates are identified by their __IsMapContainmentPredicate
    // flag stamped by the IFC importer — no predicate-name hardcoding.
    const thingMap = new Map(things.map((t) => [t.Id, t]));
    const skipPredicates = new Set<string>();
    for (const [id, t] of thingMap) {
      if (isContainmentPredicate(t.Properties)) skipPredicates.add(id);
    }
    for (const r of relationships) {
      if (skipPredicates.has(r.PredicateId)) continue;
      if (surfaceIds.has(r.SubjectId)) keepIds.add(r.TargetId);
      if (surfaceIds.has(r.TargetId)) keepIds.add(r.SubjectId);
    }
    const gt = things.filter((t) => keepIds.has(t.Id));
    const gr = relationships.filter(
      (r) => keepIds.has(r.SubjectId) && keepIds.has(r.TargetId),
    );

    return { graphThings: gt, graphRelationships: gr };
  }, [mapEnabled, showAllThings, hideOrphanSites, things, relationships]);

  // Filter for graph display — show matched nodes + their direct neighbors for context
  const searchOptions = useMemo(
    () => ({ caseSensitive, exactMatch, useRegex }),
    [caseSensitive, exactMatch, useRegex],
  );
  const { filteredThings, filteredRelationships, matchCount } = useMemo(
    () => filterGraph(searchQuery, graphThings, graphRelationships, searchOptions),
    [searchQuery, searchOptions, graphThings, graphRelationships],
  );

  return (
    <div className="h-full relative">
      {/* Search bar */}
      <div className="absolute top-3 left-3 z-10 flex items-center gap-2 bg-zinc-800/80 backdrop-blur rounded-lg px-3 py-1.5">
        <Search size={14} className="text-zinc-400 flex-shrink-0" />
        <input
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          placeholder={useRegex ? 'Regex pattern...' : 'Search things (comma = list)...'}
          className="bg-transparent text-sm text-zinc-200 placeholder-zinc-500 focus:outline-none w-56"
        />
        <button
          onClick={() => setCaseSensitive((v) => !v)}
          title="Match case"
          className={`px-1 py-0.5 text-xs font-semibold rounded transition-colors flex-shrink-0 ${
            caseSensitive
              ? 'bg-blue-600 text-white'
              : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
          }`}
        >
          Aa
        </button>
        <button
          onClick={() => setExactMatch((v) => !v)}
          title="Exact match"
          className={`px-1 py-0.5 text-xs font-semibold rounded transition-colors flex-shrink-0 ${
            exactMatch
              ? 'bg-blue-600 text-white'
              : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
          }`}
        >
          =
        </button>
        <button
          onClick={() => setUseRegex((v) => !v)}
          title="Regular expression"
          className={`px-1 py-0.5 text-xs font-semibold rounded transition-colors flex-shrink-0 ${
            useRegex
              ? 'bg-blue-600 text-white'
              : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
          }`}
        >
          .*
        </button>
        {searchQuery && (
          <>
            <button
              onClick={() => setSearchQuery('')}
              title="Clear search"
              className="text-zinc-500 hover:text-zinc-300 transition-colors flex-shrink-0"
            >
              <X size={14} />
            </button>
            <span className="text-xs text-zinc-500 flex-shrink-0">
              {matchCount} found
            </span>
          </>
        )}
        <div className="w-px h-4 bg-zinc-600 flex-shrink-0" />
        <button
          onClick={() => setShowCreateThing((v) => !v)}
          title="Create thing"
          className={`p-0.5 rounded transition-colors flex-shrink-0 ${
            showCreateThing
              ? 'bg-emerald-600 text-white'
              : 'text-zinc-500 hover:text-zinc-300 hover:bg-zinc-700'
          }`}
        >
          <Plus size={14} />
        </button>
      </div>
      {showCreateThing && (
        <div className="absolute top-12 left-3 z-10 flex items-center gap-2 bg-zinc-800/90 backdrop-blur rounded-lg px-3 py-1.5">
          <input
            autoFocus
            value={newThingName}
            onChange={(e) => setNewThingName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') handleCreateThing();
              if (e.key === 'Escape') { setShowCreateThing(false); setNewThingName(''); }
            }}
            placeholder="New thing name..."
            disabled={creatingThing}
            className="bg-transparent text-sm text-zinc-200 placeholder-zinc-500 focus:outline-none w-48"
          />
          <button
            onClick={handleCreateThing}
            disabled={!newThingName.trim() || creatingThing}
            className="p-0.5 rounded text-emerald-400 hover:bg-emerald-500/20 disabled:opacity-30 transition-colors flex-shrink-0"
            title="Create"
          >
            {creatingThing ? <Loader2 size={14} className="animate-spin" /> : <Plus size={14} />}
          </button>
          <button
            onClick={() => { setShowCreateThing(false); setNewThingName(''); }}
            className="text-zinc-500 hover:text-zinc-300 transition-colors flex-shrink-0"
            title="Cancel"
          >
            <X size={14} />
          </button>
        </div>
      )}

      {/* Top-right: model name + switch / logout */}
      <div className="absolute top-3 right-3 z-10 flex items-center gap-2 bg-zinc-800/80 backdrop-blur rounded-lg px-3 py-1.5">
        {modelName && <span className="text-xs text-zinc-400 mr-1">{modelName}</span>}
        {mapEnabled && (
          <button
            onClick={() => {
              if (togglingShowAll) return;
              setTogglingShowAll(true);
              // Defer toggle so spinner renders before the expensive recompute
              setTimeout(() => {
                toggleShowAllThings();
                // Clear after a frame to let React finish the re-render
                requestAnimationFrame(() => setTogglingShowAll(false));
              }, 0);
            }}
            disabled={togglingShowAll}
            title={showAllThings ? 'Show surface things only' : 'Show all things'}
            className={`p-1.5 rounded transition-colors ${
              showAllThings
                ? 'bg-blue-600 text-white'
                : 'text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700'
            }`}
          >
            {togglingShowAll ? <Loader2 size={14} className="animate-spin" /> : <Layers size={14} />}
          </button>
        )}
        <button
          onClick={switchModel}
          title="Switch model"
          className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
        >
          <ArrowLeftRight size={14} />
        </button>
        <button
          onClick={logout}
          title="Log out"
          className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
        >
          <LogOut size={14} />
        </button>
      </div>

      {/* Loading phase indicator — top center, clear of controls */}
      {loadingPhase !== 'idle' && loadingPhase !== 'done' && (
        <div className="absolute top-14 left-1/2 -translate-x-1/2 z-10 flex items-center gap-2 bg-zinc-800/90 backdrop-blur rounded-lg px-3 py-1.5">
          <Loader2 size={14} className="animate-spin text-blue-400" />
          <span className="text-xs text-zinc-300">
            {loadingPhase === 'surface' && 'Loading surface objects...'}
            {loadingPhase === 'remaining' && 'Loading remaining objects...'}
          </span>
        </div>
      )}

      <ErrorBoundary>
        <SigmaCanvas things={filteredThings} relationships={filteredRelationships} searchQuery={searchQuery} searchOptions={searchOptions} hasGeoNodes={hasGeoNodes} />
      </ErrorBoundary>

      {/* Radial predicate menu — positioned over the graph */}
      <RadialPredicateMenu />

      {/* Node context menu — positioned over the graph */}
      <NodeContextMenu
        things={things}
        relationships={relationships}
        onDeleteThing={(id, name) => setDeleteConfirm({ type: 'thing', id, name })}
      />

      {/* Detail panel — works in both 2D and 3D modes */}
      {detailThing && (
        <ResizablePanel>
          <NodeDetailPanel
            thing={detailThing}
            relationships={relationships}
            allThings={thingMap}
            onClose={() => selectNode(null)}
            onSelectNode={selectNode}
            onDeleteProperty={handleDeleteProperty}
            onPropertySet={loadData}
            statesVersion={statesVersion}
          />
        </ResizablePanel>
      )}

      {detailRelationship && (
        <ResizablePanel>
          <EdgeDetailPanel
            relationship={detailRelationship}
            allThings={thingMap}
            onClose={() => selectEdge(null)}
            onSelectNode={(id) => {
              selectEdge(null);
              selectNode(id);
            }}
            onDeleteRelationship={(id) =>
              setDeleteConfirm({ type: 'relationship', id, name: detailRelationship.Name })
            }
            onDeleteProperty={handleDeleteRelProperty}
            onPropertySet={loadData}
            statesVersion={statesVersion}
          />
        </ResizablePanel>
      )}

      <ConfirmDialog
        open={deleteConfirm !== null}
        title={`Delete ${deleteConfirm?.type || ''}`}
        message={`Are you sure you want to delete "${deleteConfirm?.name}"? This cannot be undone.`}
        confirmLabel="Delete"
        danger
        onConfirm={deleteConfirm?.type === 'thing' ? handleDeleteThing : handleDeleteRelationship}
        onCancel={() => setDeleteConfirm(null)}
      />
    </div>
  );
}
