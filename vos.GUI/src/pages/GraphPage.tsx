import { useEffect, useState, useMemo, useRef } from 'react';
import { SigmaCanvas } from '../components/graph/SigmaCanvas';
import { GraphSearchBar } from '../components/graph/GraphSearchBar';
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
import { reloadModelData } from '../hooks/useModelData';
import { useGraphData } from '../hooks/useGraphData';
import { toast } from '../components/common/Toast';
import { ConfirmDialog } from '../components/common/ConfirmDialog';
import type { VosThing, VosRelationship } from '../types/vos';
import { useAuth } from '../hooks/useAuth';
import { LogOut, ArrowLeftRight } from 'lucide-react';
import { ThemeToggleButton } from '../components/common/ThemeToggleButton';

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

  const [detailThing, setDetailThing] = useState<VosThing | null>(null);
  const [detailRelationship, setDetailRelationship] = useState<VosRelationship | null>(null);
  const thingMap = useMemo(() => new Map(things.map((t) => [t.Id, t])), [things]);
  const thingMapRef = useRef(thingMap);
  thingMapRef.current = thingMap;

  useEffect(() => {
    useUiStore.getState().clearPredicateIds();
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

  const {
    filteredThings, filteredRelationships, matchCount, searchOptions,
  } = useGraphData({
    things, relationships, searchQuery, caseSensitive, exactMatch, useRegex,
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

      {/* Top-right: model name + theme toggle + switch / logout */}
      <div className="absolute top-3 right-3 z-10 flex items-center gap-2 bg-zinc-800/80 backdrop-blur rounded-lg px-3 py-1.5">
        {modelName && <span className="text-xs text-zinc-400 mr-1">{modelName}</span>}
        <ThemeToggleButton />
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

      <ErrorBoundary>
        <SigmaCanvas things={filteredThings} relationships={filteredRelationships} searchQuery={searchQuery} searchOptions={searchOptions} />
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
