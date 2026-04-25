import { lazy, Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { apiClient } from '../api/client';
import { useAuth } from '../hooks/useAuth';
import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { useUiStore } from '../stores/uiStore';
import { NodeDetailPanel } from '../components/panels/NodeDetailPanel';
import { ResizablePanel } from '../components/panels/ResizablePanel';
import { toast } from '../components/common/Toast';
import type { VosThing, VosRelationship } from '../types/vos';
import type { FragmentsMapping } from '../components/model/FragmentsViewer';

const FragmentsViewer = lazy(() =>
  import('../components/model/FragmentsViewer').then((m) => ({ default: m.FragmentsViewer })),
);

type ViewerState =
  | { status: 'loading' }
  | { status: 'empty' }
  | { status: 'error'; message: string }
  | {
      status: 'ready';
      bytes: ArrayBuffer;
      mapping: FragmentsMapping;
      things: VosThing[];
      relationships: VosRelationship[];
    };

// Build the IFC-GlobalId → VosThing-Id map by scanning the broker's authoritative
// thing list rather than the pre-baked .mapping.json sidecar (which drifts whenever
// the seed is regenerated — Bug #5298 follow-up).
function buildMappingFromThings(things: VosThing[]): FragmentsMapping {
  const map: FragmentsMapping = {};
  for (const t of things) {
    const ifcId = t.Properties?.ifcGlobalId;
    if (typeof ifcId === 'string' && ifcId.length > 0) map[ifcId] = t.Id;
  }
  return map;
}

export function ModelPage() {
  const { modelId } = useAuth();
  const [state, setState] = useState<ViewerState>({ status: 'loading' });
  const [detailThing, setDetailThing] = useState<VosThing | null>(null);

  const selectedNodeId = useUiStore((s) => s.selectedNodeId);
  const selectNode = useUiStore((s) => s.selectNode);

  const loadModelData = useCallback(async () => {
    const [bytes, things, relationships] = await Promise.all([
      apiClient.getBytes('/api/model/fragments'),
      thingApi.getAll().catch(() => [] as VosThing[]),
      relationshipApi.getAll().catch(() => [] as VosRelationship[]),
    ]);
    return { bytes, things, relationships };
  }, []);

  // Re-fetch whenever the JWT-scoped model changes (e.g. via /api/auth/switch-model).
  useEffect(() => {
    let cancelled = false;
    setState({ status: 'loading' });
    selectNode(null);

    loadModelData()
      .then(({ bytes, things, relationships }) => {
        if (cancelled) return;
        if (bytes === null) setState({ status: 'empty' });
        else setState({
          status: 'ready',
          bytes,
          mapping: buildMappingFromThings(things),
          things,
          relationships,
        });
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setState({
          status: 'error',
          message: err instanceof Error ? err.message : 'Failed to load model',
        });
      });
    return () => {
      cancelled = true;
    };
  }, [modelId, loadModelData, selectNode]);

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
        if (cancelled) return;
        if (state.status === 'ready') {
          setDetailThing(state.things.find((t) => t.Id === selectedNodeId) ?? null);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [selectedNodeId, state]);

  const things = state.status === 'ready' ? state.things : null;
  const relationships = state.status === 'ready' ? state.relationships : null;

  const thingMap = useMemo(
    () => new Map((things ?? []).map((t) => [t.Id, t])),
    [things],
  );

  const handlePick = useCallback((vosGuid: string | null) => selectNode(vosGuid), [selectNode]);

  const reload = useCallback(async () => {
    try {
      const { bytes, things: t, relationships: r } = await loadModelData();
      if (bytes === null) {
        setState({ status: 'empty' });
        return;
      }
      setState({ status: 'ready', bytes, mapping: buildMappingFromThings(t), things: t, relationships: r });
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Failed to reload model');
    }
  }, [loadModelData]);

  const handleDeleteProperty = useCallback(async (thingId: string, propertyName: string) => {
    try {
      await thingApi.deleteProperty(thingId, propertyName);
      toast.success(`Deleted property: ${propertyName}`);
      await reload();
    } catch (err) {
      toast.error(err instanceof Error ? err.message : 'Delete failed');
    }
  }, [reload]);

  return (
    <div className="h-full flex flex-col p-6">
      <header className="mb-6">
        <h1 className="text-2xl font-bold text-zinc-900 dark:text-white">Model</h1>
        <p className="text-sm text-zinc-600 dark:text-zinc-400">
          3D viewer for the IFC-derived Fragments artifact. Click an element to inspect.
        </p>
      </header>

      {state.status === 'ready' ? (
        <div className="flex-1 flex min-h-0 rounded-lg overflow-hidden border border-zinc-200 dark:border-zinc-700 relative">
          <div className="flex-1 min-w-0">
            <Suspense fallback={<LoadingPlaceholder />}>
              <FragmentsViewer
                fragmentsBytes={state.bytes}
                mapping={state.mapping}
                onPick={handlePick}
              />
            </Suspense>
          </div>
          {detailThing && relationships && (
            <ResizablePanel>
              <NodeDetailPanel
                thing={detailThing}
                relationships={relationships}
                allThings={thingMap}
                onClose={() => selectNode(null)}
                onSelectNode={selectNode}
                onDeleteProperty={handleDeleteProperty}
                onPropertySet={reload}
              />
            </ResizablePanel>
          )}
        </div>
      ) : state.status === 'loading' ? (
        <LoadingPlaceholder />
      ) : state.status === 'error' ? (
        <ErrorPlaceholder message={state.message} />
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
