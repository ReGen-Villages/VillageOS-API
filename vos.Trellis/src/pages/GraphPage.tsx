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
import { Upload } from 'lucide-react';
import { applyTypeFilter } from '../utils/typeFilter';
import type { DeletableEntity } from './graphDeletions';
import { deletePropertyOf } from './propertyDeletion';
import { useSelectedThing } from '../hooks/useSelectedThing';

// Loaded lazily, so the search bar, type filter and controls appear before Sigma's start holds the main thread
// on a very large model, and a person can hide heavy types without the whole set ever being drawn.
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
  const [deleteConfirm, setDeleteConfirm] = useState<{ type: DeletableEntity; id: string; name: string } | null>(null);
  const [showCreateThing, setShowCreateThing] = useState(false);
  const [newThingName, setNewThingName] = useState('');
  const [newThingIsAType, setNewThingIsAType] = useState(false);
  const [creatingThing, setCreatingThing] = useState(false);

  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const selectedEdgeId = useUiStore((s) => s.selectedEdgeId);
  const selectNode = useUiStore((s) => s.selectNode);
  const selectEdge = useUiStore((s) => s.selectEdge);
  const statesVersion = useUiStore((s) => s.statesVersion);
  const hiddenTypeIds = useUiStore((s) => s.hiddenTypeIds);

  // Hidden types go before search and graph building, so drawing costs only what is visible.
  const visible = useMemo(
    () => applyTypeFilter(things, relationships, hiddenTypeIds),
    [things, relationships, hiddenTypeIds],
  );

  const thingMap = useMemo(() => new Map(things.map((t) => [t.Id, t])), [things]);
  const detailThing = useSelectedThing(selectedNodeId, thingMap);

  const detailRelationship = useMemo(
    () => (selectedEdgeId ? relationships.find((r) => r.Id === selectedEdgeId) ?? null : null),
    [selectedEdgeId, relationships],
  );

  useEffect(() => {
    useUiStore.getState().clearPredicateIds();
  }, []);

  // Mounted a moment later, so the controls paint first: on a very large model Sigma's start holds the main
  // thread for seconds, and the type filter must be reachable before it.
  const [mountSigma, setMountSigma] = useState(false);
  useEffect(() => {
    const id = setTimeout(() => setMountSigma(true), 0);
    return () => clearTimeout(id);
  }, []);

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
    await deletePropertyOf('thing', thingId, propertyName);
  };

  const handleDeleteRelationshipProperty = async (relationshipId: string, propertyName: string) => {
    await deletePropertyOf('relationship', relationshipId, propertyName);
  };

  const handleCreateThing = async () => {
    if (!newThingName.trim() || creatingThing) return;
    setCreatingThing(true);
    try {
      await thingApi.create(newThingName.trim(), newThingIsAType);
      toast.success(t('graph.toast.thingCreated', { name: newThingName.trim() }));
      setNewThingName('');
      setNewThingIsAType(false);
      setShowCreateThing(false);
      reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : t('graph.toast.createThingFailed'));
    } finally {
      setCreatingThing(false);
    }
  };

  // Applying a fragment is safe to repeat, and what it creates arrives on the stream, so only a reload is asked for.
  const fragmentInputReference = useRef<HTMLInputElement>(null);
  const handleImportFragment = async (file: File) => {
    try {
      const result = await modelApi.applyFragment(await file.text());
      toast.success(
        t('graph.toast.fragmentApplied', {
          created: result.thingsCreated,
          updated: result.thingsUpdated,
          relationships: result.relationshipsCreated,
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
        newThingIsAType={newThingIsAType} setNewThingIsAType={setNewThingIsAType}
        creatingThing={creatingThing} onCreateThing={handleCreateThing}
      />

      {/* Above the filter cluster, so a tall predicate list cannot cover it. */}
      <div className="absolute top-3 right-3 z-20 flex items-center gap-2 bg-zinc-800/80 backdrop-blur rounded-lg px-3 py-1.5">
        <input
          ref={fragmentInputReference}
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
          onClick={() => fragmentInputReference.current?.click()}
          title={t('graph.actions.importFragment')}
          className="p-1.5 rounded text-zinc-500 hover:text-zinc-200 hover:bg-zinc-700 transition-colors"
        >
          <Upload size={14} />
        </button>
      </div>

      {/* Each panel scrolls its own list, so neither pushes the other's header off screen, and the height
          leaves a band at the top for the control surface. */}
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

      <RadialPredicateMenu />

      <NodeContextMenu
        things={things}
        relationships={relationships}
        onDeleteThing={(id, name) => setDeleteConfirm({ type: 'thing', id, name })}
      />

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
            onDeleteProperty={handleDeleteRelationshipProperty}
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

/** Plain elements and no canvas, so the page paints and the type filter can be used while Sigma loads. */
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
