import { lazy, Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { apiClient } from '../api/client';
import { useAuth } from '../hooks/useAuth';
import { thingApi } from '../api/thingApi';
import { useUiStore } from '../stores/uiStore';
import { useModelStore } from '../stores/modelStore';
import { reloadModelData } from '../hooks/useModelData';
import { NodeDetailPanel } from '../components/panels/NodeDetailPanel';
import { ResizablePanel } from '../components/panels/ResizablePanel';
import { TypeFilterPanel } from '../components/panels/TypeFilterPanel';
import { toast } from '../components/common/Toast';
import type { VosThing } from '../types/vos';
import type { BimFragmentsMapping } from '../components/model/BimFragmentsViewer';
import { applyTypeFilter } from '../utils/typeFilter';

const BimFragmentsViewer = lazy(() =>
  import('../components/model/BimFragmentsViewer').then((m) => ({ default: m.BimFragmentsViewer })),
);

type BimFragmentsState =
  | { status: 'loading' }
  | { status: 'empty' }
  | { status: 'error'; message: string }
  | { status: 'ready'; bytes: ArrayBuffer };

// Build the IFC-GlobalId → VosThing-Id map by scanning the Mycelium's authoritative
// thing list rather than the pre-baked .mapping.json sidecar (which drifts whenever
// the seed is regenerated — Bug #5298 follow-up).
function buildMappingFromThings(things: VosThing[]): BimFragmentsMapping {
  const map: BimFragmentsMapping = {};
  for (const t of things) {
    const ifcId = t.Properties?.ifcGlobalId;
    if (typeof ifcId === 'string' && ifcId.length > 0) map[ifcId] = t.Id;
  }
  return map;
}

export function ModelPage() {
  const { modelId } = useAuth();
  const things = useModelStore((s) => s.things);
  const relationships = useModelStore((s) => s.relationships);
  const [bimFragments, setBimFragments] = useState<BimFragmentsState>({ status: 'loading' });
  const [detailThing, setDetailThing] = useState<VosThing | null>(null);

  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const selectNode = useUiStore((s) => s.selectNode);
  const hiddenTypeIds = useUiStore((s) => s.hiddenTypeIds);

  // Feature #5362 — translate hidden type Thing ids → IFC GlobalIds whose
  // Fragments instances should be hidden in the 3D scene. Bug #5384: delegate
  // to applyTypeFilter so the Model viewer hides the SAME set of Things as the
  // Graph page — including type-Things themselves (their own IFC geometry) and
  // the synthetic NO_TYPE_ID bucket (untyped Things with IFC geometry, e.g.
  // IfcDistributionPort). Rolling our own loop here previously skipped both.
  const hiddenIfcGuids = useMemo(() => {
    if (hiddenTypeIds.size === 0) return [];
    const visible = new Set(
      applyTypeFilter(things, relationships, hiddenTypeIds).things.map((t) => t.Id),
    );
    const out: string[] = [];
    for (const t of things) {
      if (visible.has(t.Id)) continue;
      const guid = t.Properties?.ifcGlobalId;
      if (typeof guid === 'string' && guid.length > 0) out.push(guid);
    }
    return out;
  }, [things, relationships, hiddenTypeIds]);

  // Re-fetch the .frag whenever the JWT-scoped model changes (e.g. via
  // /api/auth/switch-model). The thing/relationship arrays come from the
  // app-shell-level useModelData hook (Feature #5329).
  useEffect(() => {
    let cancelled = false;
    setBimFragments({ status: 'loading' });
    selectNode(null);

    apiClient.getBytes('/api/model/bim/fragments')
      .then((bytes) => {
        if (cancelled) return;
        setBimFragments(bytes === null ? { status: 'empty' } : { status: 'ready', bytes });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setBimFragments({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load model',
        });
      });
    return () => {
      cancelled = true;
    };
  }, [modelId, selectNode]);

  // Fetch the full thing (with inherited properties) when selection changes —
  // matches the GraphPage pattern so NodeDetailPanel sees the same shape from both views.
  useEffect(() => {
    if (!selectedNodeId) {
      setDetailThing(null);
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const thing = await thingApi.get(selectedNodeId);
        if (!cancelled) setDetailThing(thing);
      } catch {
        if (!cancelled) setDetailThing(things.find((t) => t.Id === selectedNodeId) ?? null);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [selectedNodeId, things]);

  const mapping = useMemo(() => buildMappingFromThings(things), [things]);
  const thingMap = useMemo(() => new Map(things.map((t) => [t.Id, t])), [things]);
  const handlePick = useCallback((vosGuid: string | null) => selectNode(vosGuid), [selectNode]);

  const handleDeleteProperty = useCallback(async (thingId: string, propertyName: string) => {
    try {
      await thingApi.deleteProperty(thingId, propertyName);
      toast.success(`Deleted property: ${propertyName}`);
      await reloadModelData();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Delete failed');
    }
  }, []);

  return (
    <div className="h-full flex flex-col p-6">
      <header className="mb-6">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-white">Model</h1>
        <p className="text-sm text-zinc-600 dark:text-zinc-400">
          3D viewer for the IFC-derived Fragments artifact. Click an element to inspect.
        </p>
      </header>

      {bimFragments.status === 'ready' ? (
        <div className="flex-1 flex min-h-0 rounded-lg overflow-hidden border border-zinc-200 dark:border-zinc-700 relative">
          <div className="flex-1 min-w-0 relative">
            <Suspense fallback={<LoadingPlaceholder />}>
              <BimFragmentsViewer
                bimFragmentsBytes={bimFragments.bytes}
                mapping={mapping}
                onPick={handlePick}
                hiddenIfcGuids={hiddenIfcGuids}
              />
            </Suspense>
            {/* Feature #5362 — type filter overlays the 3D viewport.
                Bug #5388: container is a bounded flex column so the panel's
                inner list gets a finite height to scroll within. */}
            <div className="absolute top-3 left-3 z-10 w-72 max-w-[80vw] flex flex-col max-h-[calc(100vh-1.5rem)]">
              <TypeFilterPanel />
            </div>
          </div>
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
              />
            </ResizablePanel>
          )}
        </div>
      ) : bimFragments.status === 'loading' ? (
        <LoadingPlaceholder />
      ) : bimFragments.status === 'error' ? (
        <ErrorPlaceholder message={bimFragments.message} />
      ) : (
        <EmptyPlaceholder />
      )}
    </div>
  );
}

function LoadingPlaceholder() {
  return (
    <div
      role="status"
      aria-label="Loading model"
      data-testid="model-viewer-loading"
      className="flex-1 rounded-lg border-2 border-dashed border-zinc-300 dark:border-zinc-700 flex items-center justify-center text-sm text-zinc-500 dark:text-zinc-400"
    >
      Loading model…
    </div>
  );
}

function EmptyPlaceholder() {
  return (
    <div
      role="region"
      aria-label="Fragments viewer placeholder"
      data-testid="model-viewer-placeholder"
      className="flex-1 rounded-lg border-2 border-dashed border-zinc-300 dark:border-zinc-700 flex flex-col items-center justify-center text-center p-8"
    >
      <p className="text-lg font-medium text-zinc-700 dark:text-zinc-300">
        No Fragments artifact loaded for this model.
      </p>
      <p className="text-sm text-zinc-500 dark:text-zinc-500 mt-2 max-w-md">
        Ingest an IFC file via <code className="font-mono">vos.Tools.IfcIngest</code> to
        produce the <code className="font-mono">.frag</code> and mapping sidecar served
        to this page.
      </p>
    </div>
  );
}

function ErrorPlaceholder({ message }: { message: string }) {
  return (
    <div
      role="alert"
      data-testid="model-viewer-error"
      className="flex-1 rounded-lg border-2 border-dashed border-red-400 dark:border-red-600 flex flex-col items-center justify-center text-center p-8"
    >
      <p className="text-lg font-medium text-red-700 dark:text-red-400">
        Failed to load the Fragments artifact.
      </p>
      <p className="text-sm text-zinc-500 dark:text-zinc-400 mt-2 max-w-md break-words">{message}</p>
    </div>
  );
}
