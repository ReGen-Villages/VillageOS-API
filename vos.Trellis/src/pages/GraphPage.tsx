import { lazy, Suspense, useEffect, useState, useMemo, useRef } from 'react';
import { GraphSearchBar } from '../components/graph/GraphSearchBar';
import { NodeDetailPanel } from '../components/panels/NodeDetailPanel';
import { EdgeDetailPanel } from '../components/panels/EdgeDetailPanel';
import { ResizablePanel } from '../components/panels/ResizablePanel';
import { RadialPredicateMenu } from '../components/graph/RadialPredicateMenu';
import { NodeContextMenu } from '../components/graph/NodeContextMenu';
import { ErrorBoundary } from '../components/common/ErrorBoundary';
import { TypeFilterPanel } from '../components/panels/TypeFilterPanel';
import { PredicateFilterPanel } from '../components/panels/PredicateFilterPanel';
import { useUiStore } from '../stores/uiStore';
import { useModelStore } from '../stores/modelStore';
import { thingApi } from '../api/thingApi';
import { modelApi } from '../api/modelApi';
import { relationshipApi } from '../api/relationshipApi';
import { reloadModelData } from '../hooks/useModelData';
import { useGraphData } from '../hooks/useGraphData';
import { toast } from '../components/common/Toast';
import { ConfirmDialog } from '../components/common/ConfirmDialog';
import type { VosThing, VosRelationship } from '../types/vos';
import { useAuth } from '../hooks/useAuth';
import { LogOut, ArrowLeftRight, Upload } from 'lucide-react';
import { ThemeToggleButton } from '../components/common/ThemeToggleButton';
import { applyTypeFilter } from '../utils/typeFilter';

// Feature #5362 — SigmaCanvas is lazy-loaded so the page commits (search bar,
// type filter, top-right controls) BEFORE Sigma's mount-time work
// (buildGraph + supervisor init + initial render of 30k+ nodes) starves the
// main thread. The user can interact with the type filter immediately on cold
// start, hide the heavy types they don't want, and the page reaches an
// interactive state without ever rendering the full set.
const SigmaCanvas = lazy(() =>
  import('../components/graph/SigmaCanvas').then((m) => ({ default: m.SigmaCanvas })),
);

export function GraphPage() {
  const { logout, switchModel, modelName } = useAuth();
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const [searchQuery, setSearchQuery] = useState('');
  const [caseSensitive, setCaseSensitive] = useState(false);
  const [exactMatch, setExactMatch] = useState(false);
  const [useRegex, setUseRegex] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<{ type: 'thing' | 'relationship'; id: string; name: string } | null>(null);
  const [showCreateThing, setShowCreateThing] = useState(false);
  const [newThingName, setNewThingName] = useState('');
  const [creatingThing, setCreatingThing] = useState(false);

  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const selectedEdgeId = useUiStore((s) => s.selectedEdgeId);
  const selectNode = useUiStore((s) => s.selectNode);
  const selectEdge = useUiStore((s) => s.selectEdge);
  const statesVersion = useUiStore((s) => s.statesVersion);
  const hiddenTypeIds = useUiStore((s) => s.hiddenTypeIds);

  // Feature #5362 — drop instances of hidden types BEFORE search filtering and
  // graph build, so render cost scales with visible-only counts. This is the
  // perf fix for Bug #5361 — at 30k+ Things any per-frame render of the full
  // set saturates the main thread.
  const visible = useMemo(
    () => applyTypeFilter(things, relationships, hiddenTypeIds),
    [things, relationships, hiddenTypeIds],
  );

  const [detailThing, setDetailThing] = useState<VosThing | null>(null);
  const [detailRelationship, setDetailRelationship] = useState<VosRelationship | null>(null);
  const thingMap = useMemo(() => new Map(things.map((t) => [t.Id, t])), [things]);
  const thingMapRef = useRef(thingMap);
  thingMapRef.current = thingMap;

  useEffect(() => {
    useUiStore.getState().clearPredicateIds();
  }, []);

  // Feature #5362 — defer Sigma mount one tick so the page chrome (search
  // bar + type filter + top-right controls) commits and paints first. On a
  // 30k-node model the synchronous SigmaCanvas mount blocks the main thread
  // for several seconds; without this defer the user can never reach the
  // type filter to hide types and recover perf on cold start.
  const [mountSigma, setMountSigma] = useState(false);
  useEffect(() => {
    const id = setTimeout(() => setMountSigma(true), 0);
    return () => clearTimeout(id);
  }, []);

  // Fetch full thing detail (including inherited properties) when a node is selected.
  // Re-runs when the underlying things array refreshes so live edits surface in the panel.
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
  }, [selectedNodeId, things]);

  // Sync detailRelationship to the latest relationships array when either
  // the selection or the underlying data changes.
  useEffect(() => {
    if (!selectedEdgeId) {
      setDetailRelationship(null);
      return;
    }
    setDetailRelationship(relationships.find((r) => r.Id === selectedEdgeId) || null);
  }, [selectedEdgeId, relationships]);

  const handleDeleteThing = async () => {
    if (!deleteConfirm || deleteConfirm.type !== 'thing') return;
    try {
      await thingApi.remove(deleteConfirm.id);
      toast.success(`Deleted: ${deleteConfirm.name}`);
      selectNode(null);
      reloadModelData();
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
      reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Delete failed');
    }
    setDeleteConfirm(null);
  };

  const handleDeleteProperty = async (thingId: string, propertyName: string) => {
    try {
      await thingApi.deleteProperty(thingId, propertyName);
      toast.success(`Deleted property: ${propertyName}`);
      reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Delete failed');
    }
  };

  const handleDeleteRelProperty = async (relationshipId: string, propertyName: string) => {
    try {
      await relationshipApi.deleteProperty(relationshipId, propertyName);
      toast.success(`Deleted property: ${propertyName}`);
      reloadModelData();
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
      reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to create thing');
    } finally {
      setCreatingThing(false);
    }
  };

  // Import a fragment file ({ Name, Things, Relationships }) and upsert it into the live model.
  // Idempotent; created Things/edges animate in over SSE, so we only need to trigger a reload.
  const fragmentInputRef = useRef<HTMLInputElement>(null);
  const handleImportFragment = async (file: File) => {
    try {
      const result = await modelApi.applyFragment(await file.text());
      toast.success(
        `Fragment applied: ${result.thingsCreated} created, ${result.thingsUpdated} updated, ` +
        `${result.relationshipsCreated} relationship(s).`,
      );
      reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to apply fragment');
    }
  };

  const {
    filteredThings, filteredRelationships, matchCount, searchOptions,
  } = useGraphData({
    things: visible.things, relationships: visible.relationships,
    searchQuery, caseSensitive, exactMatch, useRegex,
  });

  return (
    <div className="h-full relative">
      <GraphSearchBar
        searchQuery={searchQuery} setSearchQuery={setSearchQuery}
        caseSensitive={caseSensitive} setCaseSensitive={setCaseSensitive}
        exactMatch={exactMatch} setExactMatch={setExactMatch}
        useRegex={useRegex} setUseRegex={setUseRegex}
        matchCount={matchCount}
        showCreateThing={showCreateThing} setShowCreateThing={setShowCreateThing}
        newThingName={newThingName} setNewThingName={setNewThingName}
        creatingThing={creatingThing} onCreateThing={handleCreateThing}
      />

      {/* Top-right: model name + theme toggle + switch / logout.
          z-20 keeps this control surface above the filter cluster (z-10) so a
          tall predicate list can never render over the seed name / switch /
          logout controls (Bug: filter panel obscured the control surface). */}
      <div className="absolute top-3 right-3 z-20 flex items-center gap-2 bg-zinc-800/80 backdrop-blur rounded-lg px-3 py-1.5">
        {modelName && <span className="text-xs text-zinc-400 mr-1">{modelName}</span>}
        <ThemeToggleButton />
        <input
          ref={fragmentInputRef}
          type="file"
          accept="application/json,.json"
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) handleImportFragment(file);
            e.target.value = '';
          }}
        />
        <button
          onClick={() => fragmentInputRef.current?.click()}
          title="Import fragment"
          className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
        >
          <Upload size={14} />
        </button>
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

      {/* Filter cluster (Feature #5362) — bottom-right, vertically aligned
          with the toolbar in the bottom-left (both at bottom-3). Type and
          predicate filters live in one region because they're functionally
          related (both control what shows in the graph).

          Bug #5388: container shares its bounded height between the two
          panels. Each panel scrolls its own list (flex-1 min-h-0) instead
          of capping at a fixed 40vh, so neither panel can push the other's
          header out of the viewport.

          The max-h reserves the top band (~4rem) for the top-right control
          surface so a tall predicate list can't grow up over the seed name /
          switch / logout controls. Paired with z-20 on that control surface
          as a belt-and-suspenders guard. */}
      <div className="absolute bottom-3 right-3 z-10 w-72 max-w-[80vw] flex flex-col gap-2 max-h-[calc(100vh-4rem)]">
        <PredicateFilterPanel />
        <TypeFilterPanel />
      </div>

      <ErrorBoundary>
        {mountSigma ? (
          <Suspense fallback={<SigmaPlaceholder />}>
            <SigmaCanvas things={filteredThings} relationships={filteredRelationships} searchQuery={searchQuery} searchOptions={searchOptions} />
          </Suspense>
        ) : (
          <SigmaPlaceholder />
        )}
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
            onDeleteThing={(id, name) => setDeleteConfirm({ type: 'thing', id, name })}
            onPropertySet={reloadModelData}
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
            onPropertySet={reloadModelData}
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

/** Lightweight placeholder shown while SigmaCanvas is loading or deferred
 *  (Feature #5362). Plain DOM — no canvas, no WebGL, no JS work — so the
 *  page chrome can paint and the user can interact with the type filter. */
function SigmaPlaceholder() {
  return (
    <div className="absolute inset-0 flex items-center justify-center text-zinc-500 text-sm pointer-events-none">
      Loading graph…
    </div>
  );
}
