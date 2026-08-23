import { lazy, Suspense, useEffect, useState, useMemo, useRef } from 'react';
import { useTranslation } from 'react-i18next';
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
import { useSubscription } from '../hooks/useSse';
import { WHOLE_MODEL } from '../types/subscription';
import { useGraphData } from '../hooks/useGraphData';
import { toast } from '../components/common/toastStore';
import { relationshipLabel } from '../utils/relationshipLabel';
import { ConfirmDialog } from '../components/common/ConfirmDialog';
import type { VosThing } from '../types/vos';
import { Upload } from 'lucide-react';
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
  useSubscription(WHOLE_MODEL);
  const { t } = useTranslation();
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

  const [fetchedThing, setFetchedThing] = useState<VosThing | null>(null);
  const thingMap = useMemo(() => new Map(things.map((t) => [t.Id, t])), [things]);

  // Both follow the selection, so neither is state: nothing to hold that the selection and the
  // loaded model do not already say.
  const detailThing = selectedNodeId ? fetchedThing : null;
  const detailRelationship = useMemo(
    () => (selectedEdgeId ? relationships.find((r) => r.Id === selectedEdgeId) ?? null : null),
    [selectedEdgeId, relationships],
  );

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
    if (!selectedNodeId) return;
    let cancelled = false;
    (async () => {
      try {
        const thing = await thingApi.get(selectedNodeId);
        if (cancelled) return;
        setFetchedThing(thing);
      } catch {
        if (!cancelled) {
          setFetchedThing(thingMap.get(selectedNodeId) ?? null);
        }
      }
    })();
    return () => { cancelled = true; };
  }, [selectedNodeId, thingMap]);

  const handleDeleteThing = async () => {
    if (!deleteConfirm || deleteConfirm.type !== 'thing') return;
    try {
      await thingApi.remove(deleteConfirm.id);
      toast.success(t('graph.toast.deleted', { name: deleteConfirm.name }));
      selectNode(null);
      reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('graph.toast.deleteFailed'));
    }
    setDeleteConfirm(null);
  };

  const handleDeleteRelationship = async () => {
    if (!deleteConfirm || deleteConfirm.type !== 'relationship') return;
    try {
      await relationshipApi.remove(deleteConfirm.id);
      toast.success(t('graph.toast.relationshipDeleted'));
      selectEdge(null);
      reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('graph.toast.deleteFailed'));
    }
    setDeleteConfirm(null);
  };

  // Neither delete touches the store: the retraction arrives on the stream, the way it reaches
  // every other client. One path, so the one other people depend on is exercised by ordinary use.
  const handleDeleteProperty = async (thingId: string, propertyName: string) => {
    try {
      await thingApi.deleteProperty(thingId, propertyName);
      toast.success(t('graph.toast.propertyDeleted', { name: propertyName }));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('graph.toast.deleteFailed'));
    }
  };

  const handleDeleteRelProperty = async (relationshipId: string, propertyName: string) => {
    try {
      await relationshipApi.deleteProperty(relationshipId, propertyName);
      toast.success(t('graph.toast.propertyDeleted', { name: propertyName }));
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('graph.toast.deleteFailed'));
    }
  };

  const handleCreateThing = async () => {
    if (!newThingName.trim() || creatingThing) return;
    setCreatingThing(true);
    try {
      await thingApi.create(newThingName.trim());
      toast.success(t('graph.toast.thingCreated', { name: newThingName.trim() }));
      setNewThingName('');
      setShowCreateThing(false);
      reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('graph.toast.createThingFailed'));
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
        t('graph.toast.fragmentApplied', {
          created: result.thingsCreated,
          updated: result.thingsUpdated,
          rels: result.relationshipsCreated,
        }),
      );
      reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('graph.toast.fragmentFailed'));
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

      {/* Top-right: graph-specific import-fragment action. Session chrome
          (theme, switch model, log out) lives in the shared sidebar footer.
          z-20 keeps this control surface above the filter cluster (z-10) so a
          tall predicate list can never render over it (Bug: filter panel
          obscured the control surface). */}
      <div className="absolute top-3 right-3 z-20 flex items-center gap-2 bg-zinc-800/80 backdrop-blur rounded-lg px-3 py-1.5">
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
          title={t('graph.actions.importFragment')}
          className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
        >
          <Upload size={14} />
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
          surface so a tall predicate list can't grow up over the import-
          fragment control. Paired with z-20 on that control surface as a
          belt-and-suspenders guard. */}
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
            onRenamed={() => reloadModelData()}
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
              setDeleteConfirm({
                type: 'relationship',
                id,
                name: relationshipLabel(detailRelationship, (thingId) => thingMap.get(thingId)?.Name),
              })
            }
            onDeleteProperty={handleDeleteRelProperty}
            statesVersion={statesVersion}
          />
        </ResizablePanel>
      )}

      <ConfirmDialog
        open={deleteConfirm !== null}
        title={t('graph.deleteDialog.title', {
          entity: deleteConfirm ? t(`graph.entity.${deleteConfirm.type}`) : '',
        })}
        message={t('graph.deleteDialog.message', { name: deleteConfirm?.name ?? '' })}
        confirmLabel={t('common.delete')}
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
      <LoadingGraphLabel />
    </div>
  );
}

function LoadingGraphLabel() {
  const { t } = useTranslation();
  return <>{t('graph.placeholder.loadingGraph')}</>;
}
